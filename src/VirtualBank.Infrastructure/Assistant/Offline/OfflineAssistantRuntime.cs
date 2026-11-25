using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Abstractions;

namespace VirtualBank.Infrastructure.Assistant.Offline;

/// <summary>
/// Deterministic tool router for offline demonstrations. This is NOT an LLM or pgvector RAG.
/// It accepts common TR/DE/EN inflections and diacritic-free keyboard input; it cannot infer arbitrary intents.
/// Tool execution always goes through the same policy gate as Semantic Kernel.
/// </summary>
public sealed partial class OfflineAssistantRuntime : IAssistantRuntime
{
    public const string HelpText =
        "Offline mode. Try: \"list my customers\", \"show customer C-1002\", \"accounts of C-1002\", " +
        "\"show credit applications\", \"show K-100\" or \"search credit policy\".";

    public string Mode => "offline";

    public async Task<string> CompleteAsync(AssistantTurn turn, IToolInvoker tools, CancellationToken cancellationToken)
    {
        var plan = Plan(turn.UserMessage);
        var language = NaturalLanguageIntent.Detect(turn.UserMessage);
        var normalized = NaturalLanguageIntent.Normalize(turn.UserMessage);
        if (plan.Count == 0 && NaturalLanguageIntent.IsBalanceRequest(normalized) &&
            NaturalLanguageIntent.IsCreditRequest(normalized) && !NaturalLanguageIntent.IsCustomerRequest(normalized))
            return NaturalLanguageIntent.UnsupportedLoanOutstandingBalance(language);
        // Offline-name resolution is deliberately scoped to the *authorized* customer list.
        // It neither guesses a customer ID from a name nor treats an individual question as "all customers".
        if (plan.Count == 0 && NaturalLanguageIntent.IsBalanceRequest(normalized) &&
            !NaturalLanguageIntent.IsDocumentRequest(normalized) &&
            !CustomerIds().IsMatch(turn.UserMessage) &&
            !NaturalLanguageIntent.IsCreditRequest(normalized))
        {
            var search = await tools.InvokeAsync("list_customers", Args(), cancellationToken);
            using var searchResult = JsonDocument.Parse(search);
            if (searchResult.RootElement.TryGetProperty("status", out var resultStatus) &&
                resultStatus.GetString() == "succeeded" &&
                searchResult.RootElement.TryGetProperty("data", out var visibleCustomers) &&
                visibleCustomers.ValueKind == JsonValueKind.Array)
            {
                var matchingIds = visibleCustomers.EnumerateArray()
                    .Where(c => IsMentionedCustomer(c, normalized))
                    .Select(c => c.GetProperty("id").GetString())
                    .OfType<string>()
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (matchingIds.Length == 1)
                {
                    var args = Args(("customer_id", matchingIds[0]));
                    var summaryResult = await tools.InvokeAsync("summarize_customer_balances", args, cancellationToken);
                    return Describe("summarize_customer_balances", args, summaryResult, language);
                }
            }
            return NaturalLanguageIntent.MissingCustomerSelection(language);
        }
        if (plan.Count == 0 && NaturalLanguageIntent.IsBalanceRequest(NaturalLanguageIntent.Normalize(turn.UserMessage)))
            return NaturalLanguageIntent.MissingCustomerSelection(language);
        if (plan.Count == 0 && NaturalLanguageIntent.IsCredentialRequest(NaturalLanguageIntent.Normalize(turn.UserMessage)))
            return language switch
            {
                AssistantLanguage.Turkish => "Parola/şifre (password/Kennwort) işlemleri için bu asistanın yetkili bir aracı yok. Kimlik bilgilerini görüntüleyemem veya değiştiremem.",
                AssistantLanguage.German => "Für Passwort-/Kennwortänderungen gibt es kein freigegebenes Tool. Ich kann Anmeldedaten weder anzeigen noch ändern.",
                _ => "This assistant has no authorized password/credential tool. I cannot view or change credentials.",
            };
        if (plan.Count == 0)
            return language switch
            {
                AssistantLanguage.Turkish => "Bu isteği çevrimdışı modda eşleştiremedim. Örnek: 'müşterilerimi göster', 'C-1002 hesabı', 'kredi başvurularımı göster' veya 'kredi politikası'.",
                AssistantLanguage.German => "Ich konnte die Anfrage im Offline-Modus nicht zuordnen. Beispiele: 'Meine Kunden', 'K-100 anzeigen', 'Kreditrichtlinie'.",
                _ => HelpText,
            };

        var answer = new StringBuilder();
        foreach (var (tool, arguments) in plan)
        {
            var result = await tools.InvokeAsync(tool, arguments, cancellationToken);
            if (answer.Length > 0) answer.AppendLine();
            answer.AppendLine(Describe(tool, arguments, result, language));
        }
        return answer.ToString().TrimEnd();
    }

    /// <summary>Read-only intent classification; NEVER an authorization decision.</summary>
    public static IReadOnlyList<(string Tool, IReadOnlyDictionary<string, string?> Arguments)> Plan(string message)
    {
        var text = NaturalLanguageIntent.Normalize(message);
        var applications = ApplicationIds().Matches(message).Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
        var customers = CustomerIds().Matches(message).Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
        var plan = new List<(string, IReadOnlyDictionary<string, string?>)>();
        var wantsDocuments = NaturalLanguageIntent.IsDocumentRequest(text);

        // Document-policy questions should never be misread as commands to approve an application.
        if (!wantsDocuments && NaturalLanguageIntent.HasAnyStem(text, "approve", "genehmig", "freigeb", "onayla") && applications.Count > 0)
        {
            plan.Add(("approve_credit_application", Args(("application_id", applications[0]))));
            return plan;
        }

        var colon = message.IndexOf(':', StringComparison.Ordinal);
        if (!wantsDocuments && (NaturalLanguageIntent.HasAnyStem(text, "note", "notiz") || text.Contains("not ekle", StringComparison.Ordinal))
            && applications.Count > 0 && colon >= 0)
        {
            plan.Add(("add_credit_note", Args(("application_id", applications[0]), ("text", message[(colon + 1)..].Trim()))));
            return plan;
        }

        var balanceRequest = NaturalLanguageIntent.IsBalanceRequest(text);
        var accounts = NaturalLanguageIntent.IsAccountRequest(text);
        var customerRequest = NaturalLanguageIntent.IsCustomerRequest(text);
        // A credit balance/loan outstanding amount is not a customer ACCOUNT balance: do not guess.
        if (balanceRequest && NaturalLanguageIntent.IsCreditRequest(text) && !customerRequest && customers.Count == 0)
            return plan;

        plan.AddRange(applications.Select(id => ("get_credit_application", Args(("application_id", id)))));
        plan.AddRange(customers.Select(id => (
            balanceRequest ? "summarize_customer_balances" : accounts ? "list_customer_accounts" : "get_customer",
            Args(("customer_id", id)))));

        if (wantsDocuments)
            plan.Add(("search_documents", Args(("query", message))));

        if (plan.Count == 0)
        {
            if (balanceRequest)
            {
                // Only explicit plural or "total" requests can aggregate all accessible customers.
                // Singular questions without an ID attempt name resolution in CompleteAsync; if no
                // unique authorized name matches, the answer requests an identifier instead.
                if (customerRequest && (NaturalLanguageIntent.IsPluralCustomerRequest(text) ||
                    NaturalLanguageIntent.IsTotalBalanceRequest(text)))
                    plan.Add(("summarize_customer_balances", Args()));
                if (customerRequest && NaturalLanguageIntent.IsCreditRequest(text))
                    plan.Add(("list_credit_applications", Args()));
            }
            else
            {
                if (NaturalLanguageIntent.IsCreditRequest(text)) plan.Add(("list_credit_applications", Args()));
                if (customerRequest) plan.Add(("list_customers", Args()));
            }
        }
        return plan;
    }

    private static bool IsMentionedCustomer(JsonElement customer, string normalizedQuestion)
    {
        if (!customer.TryGetProperty("firstName", out var first) ||
            !customer.TryGetProperty("lastName", out var last)) return false;
        var firstName = NaturalLanguageIntent.Normalize(first.GetString() ?? "");
        var lastName = NaturalLanguageIntent.Normalize(last.GetString() ?? "");
        if (firstName.Length < 3 || lastName.Length < 3) return false;
        var fullName = firstName + " " + lastName;
        // A FULL name is preferred, but a unique first/last name is also accepted. Multiple matches -> ask ID.
        return ContainsName(normalizedQuestion, fullName) ||
               ContainsName(normalizedQuestion, firstName) || ContainsName(normalizedQuestion, lastName);
    }

    private static bool ContainsName(string question, string name) =>
        Regex.IsMatch(question, $@"(?<!\p{{L}}){Regex.Escape(name)}(?!\p{{L}})",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static string Describe(string tool, IReadOnlyDictionary<string, string?> arguments, string resultJson, AssistantLanguage language)
    {
        using var document = JsonDocument.Parse(resultJson);
        var root = document.RootElement;
        var status = root.GetProperty("status").GetString();
        var subject = arguments.Values.FirstOrDefault(v => v is not null && IdPattern().IsMatch(v));
        var data = root.TryGetProperty("data", out var value) ? value : default;
        return status switch
        {
            "succeeded" when tool == "search_documents" => RenderEvidence(data, language),
            "succeeded" when tool == "summarize_customer_balances" => AssistantBalanceFormatter.Format(data, language),
            "succeeded" when data.ValueKind == JsonValueKind.Array => RenderCount(tool, data.GetArrayLength(), language),
            "succeeded" => RenderDetail(tool, subject, data, language),
            "not_found" => language switch
            {
                AssistantLanguage.Turkish => "Kayıt bulunamadı veya bu kaydı görme yetkiniz yok.",
                AssistantLanguage.German => "Eintrag nicht gefunden oder nicht für Sie sichtbar.",
                _ => "Record not found or not visible to you.",
            },
            "denied" => language switch
            {
                AssistantLanguage.Turkish => "Bu işlem için gerekli erişim yetkiniz yok.",
                AssistantLanguage.German => "Diese Information ist für Sie nicht verfügbar.",
                _ => "This information is not available to you.",
            },
            "invalid" => language switch
            {
                AssistantLanguage.Turkish => "İstek parametreleri geçersiz.",
                AssistantLanguage.German => "Ungültige Anfrageparameter.",
                _ => "Invalid request parameters.",
            },
            _ => language switch
            {
                AssistantLanguage.Turkish => "İşlem tamamlanamadı; lütfen yeniden deneyin.",
                AssistantLanguage.German => "Anfrage fehlgeschlagen; bitte erneut versuchen.",
                _ => "Request failed; please try again.",
            },
        };
    }

    private static string RenderCount(string tool, int count, AssistantLanguage language)
    {
        if (count == 0) return language switch
        {
            AssistantLanguage.Turkish => "Erişiminize açık kayıt bulunamadı.",
            AssistantLanguage.German => "Keine für Sie sichtbaren Einträge gefunden.",
            _ => "No accessible records found.",
        };
        var entity = tool switch
        {
            "list_customers" => ("müşteri", "Kunden", "customers"),
            "list_credit_applications" => ("kredi başvurusu", "Kreditanträge", "credit applications"),
            "list_customer_accounts" => ("hesap", "Konten", "accounts"),
            _ => ("kayıt", "Einträge", "records"),
        };
        var prefix = language switch
        {
            AssistantLanguage.Turkish => $"{count} {entity.Item1} bulundu. Ayrıntılar tabloda gösteriliyor.",
            AssistantLanguage.German => $"{count} {entity.Item2} gefunden. Details stehen in der Tabelle.",
            _ => $"Found {count} {entity.Item3}. Details are in the table below.",
        };
        return tool == "list_customers" ? prefix + " " + NaturalLanguageIntent.AccessibleCustomerScope(language) : prefix;
    }

    private static string RenderDetail(string tool, string? identifier, JsonElement data, AssistantLanguage language)
    {
        if (data.ValueKind != JsonValueKind.Object) return data.ToString();
        var kind = tool == "get_customer" ? "customer" : tool == "get_credit_application" ? "credit" : "record";
        var title = language switch
        {
            AssistantLanguage.Turkish => kind == "customer" ? "Müşteri bilgileri" : kind == "credit" ? "Kredi başvurusu" : "Kayıt",
            AssistantLanguage.German => kind == "customer" ? "Kundendaten" : kind == "credit" ? "Kreditantrag" : "Eintrag",
            _ => kind == "customer" ? "Customer details" : kind == "credit" ? "Credit application" : "Record",
        };
        var lines = data.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null)
            .Select(p => $"{Label(p.Name, language)}: {p.Value}");
        return $"{title}{(identifier is null ? "" : " (" + identifier + ")")}\n" + string.Join("\n", lines);
    }

    private static string Label(string name, AssistantLanguage language) => (name, language) switch
    {
        ("id", AssistantLanguage.Turkish) => "Numara",
        ("firstName", AssistantLanguage.Turkish) => "Ad",
        ("lastName", AssistantLanguage.Turkish) => "Soyad",
        ("branch", AssistantLanguage.Turkish) => "Şube",
        ("customerId", AssistantLanguage.Turkish) => "Müşteri No",
        ("amount", AssistantLanguage.Turkish) => "Tutar",
        ("currency", AssistantLanguage.Turkish) => "Para birimi",
        ("status", AssistantLanguage.Turkish) => "Durum",
        ("purpose", AssistantLanguage.Turkish) => "Amaç",
        ("sensitiveFieldsMasked", AssistantLanguage.Turkish) => "Hassas alanlar maskeli",
        _ => name,
    };

    private static string RenderEvidence(JsonElement data, AssistantLanguage language)
    {
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            return NaturalLanguageIntent.NoDocumentEvidence(language);
        var result = new StringBuilder(language switch
        {
            AssistantLanguage.Turkish => "Erişiminize açık belgelerden bulunan ilgili alıntılar (otomatik özet değildir):",
            AssistantLanguage.German => "Auszüge aus Ihren zugänglichen Dokumenten (keine automatisch erzeugte Zusammenfassung):",
            _ => "Excerpts from accessible documents (not an AI-generated summary):",
        });
        foreach (var hit in data.EnumerateArray().Take(3))
        {
            var title = hit.TryGetProperty("title", out var t) ? t.GetString() ?? "Document" : "Document";
            var docId = hit.TryGetProperty("documentId", out var id) ? id.GetString() ?? "" : "";
            var page = hit.TryGetProperty("pageNumber", out var pageNumber) && pageNumber.ValueKind == JsonValueKind.Number
                ? $", p. {pageNumber.GetInt32()}" : "";
            var excerpt = hit.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "";
            // Retrieved content is untrusted text, not instructions. Never interpret it as a command.
            result.Append("\n• ").Append(title).Append(" (").Append(docId).Append(page).Append("): ")
                  .Append(excerpt.Length > 350 ? excerpt[..350] + "…" : excerpt);
        }
        return result.ToString();
    }

    private static IReadOnlyDictionary<string, string?> Args(params (string Key, string? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    [GeneratedRegex(@"\bK-[0-9]{3}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationIds();

    [GeneratedRegex(@"\bC-[0-9]{4}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CustomerIds();

    [GeneratedRegex(@"^[CK]-[0-9]{3,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
