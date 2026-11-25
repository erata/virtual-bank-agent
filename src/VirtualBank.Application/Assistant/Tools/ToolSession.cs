using VirtualBank.Application.Common.Actions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VirtualBank.Application.Assistant.Abstractions;
using VirtualBank.Application.Customers;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Customers.Queries.SummarizeCustomerBalances;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Permissions;

namespace VirtualBank.Application.Assistant.Tools;

/// <summary>
/// The tool invoker for one turn of one principal. The principal is captured here, server-side; the runtime and the
/// model only ever pass a tool name and arguments. Calls are serialized so the budgets are exact.
/// </summary>
public sealed class ToolSession(
    PrincipalContext principal, AssistantToolCatalog catalog, ToolPolicyGate gate, IAssistantDiagnostics diagnostics,
    AssistantLanguage language = AssistantLanguage.English, string? originalUserMessage = null) : IToolInvoker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<ToolCallRecord> _records = [];
    private int _writes;
    private readonly List<DocumentCitation> _citations = [];
    private readonly List<AssistantDataTable> _tables = [];
    public IReadOnlyList<DocumentCitation> Citations => _citations.ToArray();
    public IReadOnlyList<AssistantDataTable> Tables => _tables.ToArray();

    /// <summary>Typed authorized balance summary; never derived from model text.</summary>
    private readonly List<CustomerBalanceOverviewView> _balanceOverviews = [];
    public IReadOnlyList<CustomerBalanceOverviewView> BalanceOverviews => _balanceOverviews.ToArray();

    /// <summary>Confirmation cards prepared in this turn; the user confirms them outside the model turn.</summary>
    private readonly List<PendingActionView> _actions = [];
    public IReadOnlyList<PendingActionView> Actions => _actions.ToArray();

    /// <summary>The caller's own profile as returned by get_my_profile (already masked per permission).</summary>
    public CustomerDetailView? OwnProfile { get; private set; }

    public IReadOnlyList<ToolCallRecord> Records
    {
        get
        {
            lock (_records)
            {
                return _records.ToList();
            }
        }
    }

    public async Task<string> InvokeAsync(string toolName, IReadOnlyDictionary<string, string?> arguments, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var tool = catalog.Find(toolName);
            var gateResult = gate.Evaluate(principal, tool, arguments, _records.Count, _writes);
            // If a user explicitly named customer IDs, a model must not broaden a balance request to
            // "all accessible customers" or a different ID before *any* account data is read.
            if (gateResult.IsAllowed && toolName == "summarize_customer_balances" &&
                !string.IsNullOrWhiteSpace(originalUserMessage))
            {
                var requested = Regex.Matches(originalUserMessage, @"\bC-[0-9]{4}\b",
                    RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50))
                    .Select(m => m.Value.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
                if (requested.Count > 0 &&
                    (!arguments.TryGetValue("customer_id", out var selectedId) ||
                     !requested.Contains(selectedId?.Trim().ToUpperInvariant() ?? "")))
                    gateResult = GateResult.Reject(ToolOutcome.Invalid(AssistantNoticeCodes.InvalidToolArguments));
            }
            // If a customer ID is explicitly mentioned, never reinterpret it as "my" account
            // (which could produce an unrelated but authorized own balance).
            if (gateResult.IsAllowed && toolName.StartsWith("get_my_", StringComparison.Ordinal) &&
                principal.HasPermission(KnownPermissions.CustomerReadSelf) &&
                !string.IsNullOrWhiteSpace(originalUserMessage) &&
                Regex.IsMatch(originalUserMessage, @"\bC-[0-9]{4}\b", RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(50)))
                gateResult = GateResult.Reject(ToolOutcome.Invalid(AssistantNoticeCodes.InvalidToolArguments));
            var outcome = gateResult.IsAllowed ? await ExecuteAsync(tool!, gateResult.Arguments!, cancellationToken) : gateResult.Rejection!;

            if (outcome.Status == ToolCallStatus.Succeeded && tool?.Name == "search_documents" &&
                outcome.Data is IReadOnlyList<DocumentSearchHit> documentHits)
            {
                foreach (var hit in documentHits)
                {
                    if (!_citations.Any(c => c.DocumentId == hit.DocumentId && c.Section == hit.Section))
                        _citations.Add(new DocumentCitation(hit.DocumentId, hit.Title, hit.Section, hit.PageNumber));
                }
            }

            if (outcome.Status == ToolCallStatus.Succeeded && tool?.Name == "get_document_excerpt" &&
                outcome.Data is DocumentExcerpt documentExcerpt)
            {
                foreach (var section in documentExcerpt.Sections)
                    if (!_citations.Any(c => c.DocumentId == documentExcerpt.DocumentId && c.PageNumber == section.PageNumber))
                        _citations.Add(new DocumentCitation(documentExcerpt.DocumentId, documentExcerpt.Title,
                            "Excerpt", section.PageNumber));
            }

            if (outcome.Status == ToolCallStatus.Succeeded && (tool?.Name == "summarize_customer_balances" || tool?.Name == "get_my_balance_summary") &&
                outcome.Data is CustomerBalanceOverviewView summary)
                _balanceOverviews.Add(summary);

            if (outcome.Status == ToolCallStatus.Succeeded && tool?.Definition.Risk == ToolRisk.PreparesConfirmation &&
                outcome.Data is PendingActionView card)
                _actions.Add(card);

            if (outcome.Status == ToolCallStatus.Succeeded && tool?.Name == "get_my_profile" &&
                outcome.Data is CustomerDetailView ownProfile)
                OwnProfile = ownProfile;

            if (outcome.Status == ToolCallStatus.Succeeded && tool is not null &&
                AssistantTableProjector.Create(tool.Name, outcome.Data, language) is { } table)
                _tables.Add(table);

            if (gateResult.IsAllowed && tool!.Definition.Risk == ToolRisk.LowRiskWrite)
            {
                _writes++;
            }

            lock (_records)
            {
                // Unknown names are recorded as a constant so arbitrary model output never reaches logs or the client.
                _records.Add(new ToolCallRecord(_records.Count + 1, tool?.Name ?? "unknown", outcome.Status, outcome.NoticeCode));
            }

            return Serialize(outcome);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<ToolOutcome> ExecuteAsync(AssistantTool tool, ToolArguments arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await tool.ExecuteAsync(principal, arguments, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Details stay server-side; the model and the client only learn that the tool failed.
            diagnostics.ToolFailed(tool.Name, ex);
            return new ToolOutcome(ToolCallStatus.Failed, NoticeCode: AssistantNoticeCodes.ToolFailed);
        }
    }

    private static string Serialize(ToolOutcome outcome) => JsonSerializer.Serialize(new
    {
        status = outcome.Status switch
        {
            ToolCallStatus.Succeeded => "succeeded",
            ToolCallStatus.NotFound => "not_found",
            ToolCallStatus.Denied => "denied",
            ToolCallStatus.Invalid => "invalid",
            _ => "failed",
        },
        notice = outcome.NoticeCode,
        data = outcome.Data,
    }, Json);
}
