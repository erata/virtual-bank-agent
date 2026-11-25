using VirtualBank.Application.Assistant.Abstractions;
using VirtualBank.Application.Customers.Queries.SummarizeCustomerBalances;
using System.Text.RegularExpressions;
using VirtualBank.Application.Assistant.Authorization;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;

namespace VirtualBank.Application.Assistant.Commands.AskAssistant;

public sealed record AskAssistantCommand(PrincipalContext Principal, string? Message, string? ConversationId = null);

/// <summary>
/// Command: one assistant turn. Order: capability policy (assistant.use, human principal) → input → runtime with a
/// per-turn <see cref="ToolSession"/>. Partial success is normal: denied tool calls become notice codes next to the
/// answer instead of failing the whole request. Notices are derived from tool records, never from model text.
/// </summary>
public sealed class AskAssistantHandler(
    AssistantToolCatalog catalog,
    ToolPolicyGate gate,
    IAssistantRuntime runtime,
    IAssistantDiagnostics diagnostics,
    IResourceAuthorizationService authorization,
    IAuthorizationAudit audit,
    NoticeCatalog notices,
    AssistantLimits limits,
    IConversationStore? conversations = null)
{
    public async Task<UseCaseResult<AssistantReply>> HandleAsync(AskAssistantCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorization.AuthorizeAsync(
            command.Principal, AssistantResource.Chat, KnownPermissions.AssistantUse, cancellationToken);
        audit.Record(command.Principal, decision);
        if (!decision.IsAllowed)
        {
            return UseCaseResult<AssistantReply>.Forbidden(notices.FromDecision(decision)?.Code);
        }

        var message = command.Message?.Trim();
        if (string.IsNullOrEmpty(message) || message.Length > limits.MaxMessageLength)
        {
            return UseCaseResult<AssistantReply>.Invalid("message", $"A message must contain 1 to {limits.MaxMessageLength} characters.");
        }

        ConversationState? conversation = null;
        if (conversations is not null)
        {
            conversation = await conversations.LoadAsync(command.Principal.SubjectId, command.ConversationId, cancellationToken);
            if (conversation is null) return UseCaseResult<AssistantReply>.NotFound();
        }
        var language = NaturalLanguageIntent.Detect(message);

        // "What can I do here?" has a deterministic, permission-derived answer: no model round-trip, no tool call.
        // Runs only after the assistant.use decision above and lists only tools this principal may use.
        if (AssistantCapabilityAnswer.IsCapabilityQuestion(message))
        {
            var capabilities = AssistantCapabilityAnswer.Describe(catalog, command.Principal, language);
            if (conversation is not null)
                await conversations!.AppendTurnAsync(command.Principal.SubjectId, conversation.Id, message, capabilities, cancellationToken);
            return UseCaseResult<AssistantReply>.Success(new AssistantReply(
                capabilities, AssistantCapabilityAnswer.Mode, [], [], [], conversation?.Id, []));
        }
        // For reference-only followups, inherit a bounded amount of the SAME authenticated user's
        // previous question as scope evidence, never its prior model answer or financial data.
        var previousQuestion = conversation?.PriorUserMessages.LastOrDefault();
        var isFollowup = previousQuestion is not null && NaturalLanguageIntent.IsContextualFollowUp(message);
        var scopedQuestion = isFollowup ? previousQuestion + " " + message : message;
        var session = new ToolSession(command.Principal, catalog, gate, diagnostics, language, scopedQuestion);
        var turn = new AssistantTurn(AssistantPrompt.System, message, catalog.VisibleDefinitions(command.Principal, limits.ToolVisibility),
            limits.MaxToolCalls, conversation?.PriorUserMessages,
            NaturalLanguageIntent.IsLanguageRecognised(message) ? language : null);

        string? answer;
        string? runtimeNotice = null;
        try
        {
            answer = await runtime.CompleteAsync(turn, session, cancellationToken);
        }
        catch (AssistantRuntimeUnavailableException ex)
        {
            diagnostics.RuntimeUnavailable(runtime.Mode, ex);
            answer = null;
            runtimeNotice = AssistantNoticeCodes.ModelUnavailable;
        }

        if (answer is { Length: var length } && length > limits.MaxAnswerLength)
        {
            answer = answer[..limits.MaxAnswerLength];
        }

        var records = session.Records;
        var noticeCodes = records
            .Select(r => r.NoticeCode)
            .Append(runtimeNotice)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        // The model cannot call a hidden tool, so it cannot report the missing capability itself.
        // Only the explicit user request is inspected, never model-generated text or retrieved documents.
        // This generic hint reveals no resource existence or title.
        // If a credit tool WAS invoked and denied, its specific notice (e.g. CREDIT_NOTE_PERMISSION_REQUIRED)
        // already explains the outcome; the generic hint would only add a second, less precise reason.
        if (limits.ToolVisibility == ToolVisibilityMode.Hide &&
            AssistantIntentNotices.NeedsCreditReadHint(message, command.Principal) &&
            !records.Any(r => r.Status == ToolCallStatus.Denied && r.Tool.Contains("credit", StringComparison.Ordinal)) &&
            !noticeCodes.Contains("CREDIT_READ_REQUIRED", StringComparer.Ordinal))
            noticeCodes.Add("CREDIT_READ_REQUIRED");

        // A model (or an Offline keyword router) must not substitute a *customer list* for a money/balance request.
        // In this high-impact financial case we emit only amounts computed by the authorized C# use case;
        // we NEVER trust LLM prose/arithmetic or combine currencies.
        var normalizedQuestion = NaturalLanguageIntent.Normalize(scopedQuestion);
        var totalFollowup = isFollowup &&
            NaturalLanguageIntent.HasAnyStem(NaturalLanguageIntent.Normalize(message), "toplam", "total", "summe", "gesamt") &&
            NaturalLanguageIntent.HasAnyStem(NaturalLanguageIntent.Normalize(previousQuestion!),
                "account", "accounts", "konto", "konten", "hesap", "bakiye", "money", "para", "customer", "kunde", "musteri");
        var asksForBalance = (NaturalLanguageIntent.IsBalanceRequest(normalizedQuestion) || totalFollowup) &&
            !NaturalLanguageIntent.IsDocumentRequest(normalizedQuestion);
        var trustedBalances = asksForBalance ? ValidateBalanceScope(scopedQuestion, session.BalanceOverviews, command.Principal) : null;
        if (asksForBalance && runtimeNotice is null)
        {
            if (trustedBalances is { } authorizedBalances)
            {
                answer = AssistantBalanceFormatter.Format(authorizedBalances, language);
                // The balance text replaces model prose, so anything else the user asked for in the same message
                // must come from authorized tool data too, e.g. "my IBAN and my balance".
                if (session.OwnProfile is { Iban: { Length: > 0 } iban } &&
                    NaturalLanguageIntent.HasAnyStem(NaturalLanguageIntent.Normalize(message), "iban"))
                    answer = language switch
                    {
                        AssistantLanguage.Turkish => $"IBAN'ınız: {iban}. ",
                        AssistantLanguage.German => $"Ihre IBAN: {iban}. ",
                        _ => $"Your IBAN: {iban}. ",
                    } + answer;
            }
            else
            {
                answer = NaturalLanguageIntent.IsCreditRequest(normalizedQuestion) &&
                    !NaturalLanguageIntent.IsCustomerRequest(normalizedQuestion)
                    ? NaturalLanguageIntent.UnsupportedLoanOutstandingBalance(language)
                    : NaturalLanguageIntent.MissingCustomerSelection(language);
                if (records.Any(r => r.Status == ToolCallStatus.Denied))
                {
                    answer = language switch
                    {
                        AssistantLanguage.Turkish => "İstenen hesap bakiyelerine erişim izniniz yok; bilgi uydurmayacağım.",
                        AssistantLanguage.German => "Sie haben keinen Zugriff auf diese Kontosalden. Ich werde nichts erfinden.",
                        _ => "You do not have access to the requested account balances. I will not guess.",
                    };
                }
                if (!records.Any(r => r.Status == ToolCallStatus.Denied) &&
                    !records.Any(r => r.Status == ToolCallStatus.NotFound))
                    noticeCodes.Add(AssistantNoticeCodes.NoVerifiedFinancialData);
                else if (records.Any(r => r.Status == ToolCallStatus.NotFound))
                    answer = language switch
                    {
                        AssistantLanguage.Turkish => "Kayıt bulunamadı veya bu müşteriye erişim yetkiniz yok.",
                        AssistantLanguage.German => "Kunde nicht gefunden oder für Sie nicht sichtbar.",
                        _ => "Customer not found or not visible to you.",
                    };
            }
        }

        // Application-level evidence guard: never deliver a model-generated document answer without
        // authorized citations, even if retrieval failed, the model declined to call search, or a tool was denied.
        // Document citations come only from successful, policy-checked search_documents calls.
        var requestsDocuments = NaturalLanguageIntent.IsDocumentRequest(NaturalLanguageIntent.Normalize(message));
        // A pure listing ("which documents can I read?") is answered by authorized metadata, not by policy text.
        // The answer text is application-owned (count only); titles come from the table, never from model prose.
        var documentList = session.Tables.FirstOrDefault(t => t.SourceTool == "list_documents");
        var pureDocumentListing = documentList is not null &&
            records.Any(r => r.Tool == "list_documents" && r.Status == ToolCallStatus.Succeeded) &&
            records.All(r => r.Tool != "search_documents" && r.Tool != "get_document_excerpt");
        if (requestsDocuments && pureDocumentListing && session.Citations.Count == 0 && runtimeNotice is null)
        {
            answer = NaturalLanguageIntent.DocumentListSummary(documentList!.TotalCount, language);
        }
        else if (requestsDocuments && session.Citations.Count == 0 && runtimeNotice is null)
        {
            answer = NaturalLanguageIntent.NoDocumentEvidence(language);
            if (records.Any(r => r.Tool == "search_documents" && r.Status == ToolCallStatus.Succeeded) ||
                (records.All(r => r.Tool != "search_documents") && command.Principal.HasPermission(KnownPermissions.DocumentRead)))
                noticeCodes.Add(AssistantNoticeCodes.NoDocumentEvidence);
            if (!command.Principal.HasPermission(KnownPermissions.DocumentRead))
            {
                // "No source found" would suggest a search happened; this caller has no document access at all.
                answer = language switch
                {
                    AssistantLanguage.Turkish => "Belge okuma yetkiniz bulunmuyor; bu nedenle belgelerde arama yapamıyorum.",
                    AssistantLanguage.German => "Sie haben keine Berechtigung zum Lesen von Dokumenten; daher kann ich keine Dokumente durchsuchen.",
                    _ => "You are not permitted to read documents, so I cannot search them.",
                };
                noticeCodes.Add(AssistantNoticeCodes.ToolNotPermitted);
            }
        }
        // A bank-data answer without ANY successfully retrieved data must not be filled with model guesses.
        // This is deliberately conservative; unrelated small-talk remains possible.
        else if (!requestsDocuments && !asksForBalance &&
                 // A payment instruction may end with a clarifying question; the model cannot execute anything
                 // anyway (only confirmation cards), so its reply is not a claim about bank records.
                 !NaturalLanguageIntent.IsPaymentInstruction(NaturalLanguageIntent.Normalize(message)) &&
                 NaturalLanguageIntent.IsBankRecordRequest(message) &&
                 !records.Any(r => r.Status == ToolCallStatus.Succeeded) && runtimeNotice is null)
        {
            // Application-owned text only. A denied tool call (missing permission or human confirmation) is a
            // different outcome from "nothing was retrieved" and is reported as such; notices carry the reason.
            var notVisible = records.Any(r => r.Status == ToolCallStatus.NotFound);
            var denied = records.Any(r => r.Status == ToolCallStatus.Denied);
            answer = (notVisible, denied, language) switch
            {
                (true, _, AssistantLanguage.Turkish) => "Kayıt bulunamadı veya bu kaydı görme yetkiniz yok.",
                (true, _, AssistantLanguage.German) => "Eintrag nicht gefunden oder nicht für Sie sichtbar.",
                (true, _, _) => "Record not found or not visible to you.",
                (_, true, AssistantLanguage.Turkish) => "Bu işlem veya bilgi sizin için kullanılabilir değil.",
                (_, true, AssistantLanguage.German) => "Diese Aktion oder Information ist für Sie nicht verfügbar.",
                (_, true, _) => "This action or information is not available to you.",
                (_, _, AssistantLanguage.Turkish) => "Bu soruyu doğrulayacak yetkili banka verisi getirilmedi; tahmin yürütmeyeceğim.",
                (_, _, AssistantLanguage.German) => "Es wurden keine autorisierten Bankdaten abgerufen; ich werde keine Antwort erfinden.",
                _ => "No authorized bank records were retrieved to support an answer. I will not guess.",
            };
            if (records.Count == 0) noticeCodes.Add(AssistantNoticeCodes.NoVerifiedData);
        }
        // Model language is not a reliable description of permission scope: append an application-owned scope
        // disclosure only when an authorized customer list was actually fetched for a list request.
        if (!asksForBalance && !requestsDocuments &&
            records.Any(r => r.Tool == "list_customers" && r.Status == ToolCallStatus.Succeeded) &&
            !string.IsNullOrEmpty(answer) &&
            !answer.Contains(NaturalLanguageIntent.AccessibleCustomerScope(language), StringComparison.Ordinal))
        {
            answer += "\n" + NaturalLanguageIntent.AccessibleCustomerScope(language);
        }
        noticeCodes = noticeCodes.Distinct(StringComparer.Ordinal).ToList();

        if (conversation is not null)
            await conversations!.AppendTurnAsync(command.Principal.SubjectId, conversation.Id, message, answer, cancellationToken);
        IReadOnlyList<AssistantDataTable> renderedTables = asksForBalance
            ? trustedBalances is { } balances && AssistantTableProjector.Create(
                command.Principal.HasPermission(KnownPermissions.CustomerReadSelf) &&
                    !command.Principal.HasPermission(KnownPermissions.CustomerRead) &&
                    !command.Principal.HasPermission(KnownPermissions.CustomerReadAssigned)
                    ? "get_my_balance_summary" : "summarize_customer_balances", balances, language) is { } table
                ? [table] : []
            : session.Tables;
        return UseCaseResult<AssistantReply>.Success(new AssistantReply(answer, runtime.Mode, records, noticeCodes,
            session.Citations, conversation?.Id, renderedTables, session.Actions));
    }

    /// <summary>
    /// Defense against incorrect model scope: a request for one ID may never display an all-customers summary.
    /// Multiple explicitly requested IDs require exact one-to-one authorized tool responses; no partial totals.
    /// A singular/name request without an ID must not be silently changed into an all-customer aggregate.
    /// </summary>
    internal static CustomerBalanceOverviewView? ValidateBalanceScope(
        string message, IReadOnlyList<CustomerBalanceOverviewView> outcomes, PrincipalContext? principal = null)
    {
        if (outcomes.Count == 0) return null;
        // A self-service customer has exactly one identity-bound row; no name/ID need be spoken.
        if (principal?.HasPermission(KnownPermissions.CustomerReadSelf) == true &&
            !principal.HasPermission(KnownPermissions.CustomerRead) &&
            !principal.HasPermission(KnownPermissions.CustomerReadAssigned))
            return outcomes.Count == 1 && outcomes[0].CustomerCount == 1 &&
                outcomes[0].SelectedCustomerId is not null ? outcomes[0] : null;
        var normalized = NaturalLanguageIntent.Normalize(message);
        var requestedIds = Regex.Matches(message, @"\bC-[0-9]{4}\b", RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(50)).Select(m => m.Value.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal).ToArray();

        if (requestedIds.Length == 0)
        {
            if (NaturalLanguageIntent.IsTotalBalanceRequest(normalized) ||
                NaturalLanguageIntent.IsPluralCustomerRequest(normalized))
                return outcomes.Count == 1 && outcomes[0].IsAllAccessibleCustomers ? outcomes[0] : null;

            // No ID and no plural scope: require the uniquely selected authorized customer's
            // actual name to appear in the question. The LLM cannot guess an unrelated ID.
            if (outcomes.Count != 1) return null;
            var selected = outcomes[0];
            if (selected.IsAllAccessibleCustomers || selected.SelectedCustomerId is null ||
                selected.SelectedCustomerName is not { } customerName)
                return null;
            var names = NaturalLanguageIntent.Normalize(customerName)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return names.Any(n => n.Length >= 3 && Regex.IsMatch(normalized,
                $@"(?<!\p{{L}}){Regex.Escape(n)}(?!\p{{L}})", RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(50))) ? selected : null;
        }

        if (outcomes.Count != requestedIds.Length ||
            outcomes.Any(o => o.IsAllAccessibleCustomers || o.SelectedCustomerId is null) ||
            outcomes.Select(o => o.SelectedCustomerId!).Distinct(StringComparer.Ordinal).Count() != requestedIds.Length ||
            outcomes.Any(o => !requestedIds.Contains(o.SelectedCustomerId!, StringComparer.Ordinal)))
            return null;

        var rows = outcomes.SelectMany(o => o.Rows).ToArray();
        var grouped = rows.GroupBy(r => r.Currency, StringComparer.Ordinal)
            .Select(g => new CurrencyBalanceTotal(g.Key, g.Sum(r => r.Balance), g.Sum(r => r.AccountCount)))
            .OrderBy(t => t.Currency, StringComparer.Ordinal).ToArray();
        return new CustomerBalanceOverviewView(rows, grouped, requestedIds.Length, false);
    }
}
