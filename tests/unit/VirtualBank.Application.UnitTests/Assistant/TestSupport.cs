using VirtualBank.Application.Credit.Commands.ApproveCreditApplication;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Abstractions;
using VirtualBank.Application.Assistant.Authorization;
using VirtualBank.Application.Assistant.Commands.AskAssistant;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Credit;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Credit.Authorization;
using VirtualBank.Application.Credit.Commands.AddCreditNote;
using VirtualBank.Application.Credit.Queries.GetCreditApplication;
using VirtualBank.Application.Credit.Queries.ListCreditApplications;
using VirtualBank.Application.Customers;
using VirtualBank.Application.Customers.Abstractions;
using VirtualBank.Application.Customers.Authorization;
using VirtualBank.Application.Customers.Queries.GetCustomer;
using VirtualBank.Application.Customers.Queries.ListCustomerAccounts;
using VirtualBank.Application.Customers.Queries.ListCustomers;
using VirtualBank.Application.Customers.Queries.SummarizeCustomerBalances;
using VirtualBank.Application.UnitTests.Credit;
using VirtualBank.Application.UnitTests.Customers;
using VirtualBank.Application.UnitTests.Support;
using VirtualBank.Model.Credit;
using VirtualBank.Model.Customers;
using static VirtualBank.Application.UnitTests.Credit.CreditUseCases;

namespace VirtualBank.Application.UnitTests.Assistant;

/// <summary>
/// Stands in for a language model: it requests a fixed sequence of tool calls (possibly hostile ones) and returns a
/// fixed answer. Because the model is untrusted, scripting it is the strongest test: anything it asks for must still
/// be stopped by application code.
/// </summary>
internal sealed class ScriptedRuntime(string answer, params (string Tool, (string Key, string? Value)[] Arguments)[] calls) : IAssistantRuntime
{
    public List<string> Results { get; } = [];

    public bool Called { get; private set; }

    public AssistantTurn? LastTurn { get; private set; }

    public bool Unavailable { get; init; }

    public string Mode => "scripted";

    public async Task<string> CompleteAsync(AssistantTurn turn, IToolInvoker tools, CancellationToken cancellationToken)
    {
        Called = true;
        LastTurn = turn;
        foreach (var (tool, arguments) in calls)
        {
            Results.Add(await tools.InvokeAsync(tool, arguments.ToDictionary(a => a.Key, a => a.Value), cancellationToken));
        }

        return Unavailable ? throw new AssistantRuntimeUnavailableException("provider down") : answer;
    }

    public static (string, (string, string?)[]) Call(string tool, params (string, string?)[] arguments) => (tool, arguments);
}

internal sealed class RecordingDiagnostics : IAssistantDiagnostics
{
    public List<string> Failures { get; } = [];

    public void ToolFailed(string tool, Exception exception) => Failures.Add(tool);

    public void RuntimeUnavailable(string mode, Exception exception) => Failures.Add("runtime:" + mode);
}

internal sealed class ThrowingCustomerRepository : ICustomerRepository
{
    public Task<IReadOnlyList<Customer>> ListAsync(PrincipalContext principal, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("database unavailable");

    public Task<Customer?> FindAsync(PrincipalContext principal, string customerId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("database unavailable");

    public Task<IReadOnlyList<Account>> ListAccountsAsync(PrincipalContext principal, string customerId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("database unavailable");
}

/// <summary>
/// Wires the assistant like the composition root: real policies, real handlers and the real gate, over repositories
/// that ignore the principal (no RLS), so every denial below comes from application code.
/// </summary>
internal sealed class AssistantHarness
{
    public static readonly string Mehmet = Personas.SubjectOf("mehmet.kredit");

    public AssistantHarness(AssistantLimits? limits = null, ICustomerRepository? customers = null, ISecureDocumentSearch? documents = null,
        bool confirmationCards = false)
    {
        Limits = limits ?? new AssistantLimits();
        Customers = customers ?? new UnfilteredCustomerRepository(
            DemoCustomer("C-1002", "STR", "Jana", "Weidner", Mehmet),
            DemoCustomer("C-1003", "STR", "Oliver", "Brandt"),
            DemoCustomer("C-1004", "STR", "Selin", "Aydın"),
            DemoCustomer("C-5001", "MUC", "Hidden", "Customer"),
            DemoCustomer("C-5002", "MUC", "Assigned", "Customer", Mehmet));
        Credit = new UnfilteredRepository(
            NewApplication("K-100", CreditApplicationStatus.Submitted, Mehmet),
            NewApplication("K-200", CreditApplicationStatus.Draft, analyst: null));

        var customerReader = new CustomerReader(Customers, PolicyEngine, Audit);
        var creditReader = new CreditApplicationReader(Credit, PolicyEngine, Audit);
        var addNote = new AddCreditNoteHandler(creditReader, Credit, PolicyEngine, Audit, Notices);
        CreditApproval? approval = null;
        CreditNoteCards? noteCards = null;
        if (confirmationCards)
        {
            var factory = new PendingActionFactory(Store, TimeProvider.System);
            approval = new CreditApproval(creditReader, PolicyEngine, Audit, Notices, factory);
            noteCards = new CreditNoteCards(creditReader, PolicyEngine, Audit, Notices, factory, addNote);
            Confirmation = new ActionConfirmation(Store, [new CreditNoteAction(noteCards)], TimeProvider.System);
        }
        Catalog = new AssistantToolCatalog(
            new ListCustomersHandler(customerReader),
            new GetCustomerHandler(customerReader, PolicyEngine, Audit),
            new ListCustomerAccountsHandler(customerReader, Customers),
            new ListCreditApplicationsHandler(creditReader, Notices),
            new GetCreditApplicationHandler(creditReader, Notices),
            addNote, documents,
            new SummarizeCustomerBalancesHandler(new ListCustomersHandler(customerReader),
                new ListCustomerAccountsHandler(customerReader, Customers)),
            creditApproval: approval, noteCards: noteCards);
    }

    private static Customer DemoCustomer(string id, string branch, string first, string last,
        params string[] analystIds) => new(id, branch, first, last, "private",
            $"{id}@example.test", "+49 0", "DE00" + id, new DateOnly(1980, 1, 1),
            analystIds.ToHashSet(StringComparer.Ordinal));

    public static ResourceAuthorizationService PolicyEngine { get; } =
        new([.. CustomerPolicies.Create(), .. CreditPolicies.Create(), .. AssistantPolicies.Create()]);

    public static NoticeCatalog Notices { get; } = new([.. CreditPolicies.NoticeRules, .. AssistantPolicies.NoticeRules]);

    public AssistantLimits Limits { get; }

    public ICustomerRepository Customers { get; }

    public UnfilteredRepository Credit { get; }

    public RecordingAudit Audit { get; } = new();

    public RecordingDiagnostics Diagnostics { get; } = new();

    public AssistantToolCatalog Catalog { get; }

    public InMemoryPendingActionStore Store { get; } = new(TimeProvider.System);

    /// <summary>Only with confirmationCards: the user's own confirmation path (outside any model turn).</summary>
    public ActionConfirmation? Confirmation { get; }

    public Task<Common.UseCaseResult<AssistantReply>> AskAsync(string persona, IAssistantRuntime runtime, string? message = "question") =>
        new AskAssistantHandler(Catalog, new ToolPolicyGate(Limits, Notices), runtime, Diagnostics, PolicyEngine, Audit, Notices, Limits)
            .HandleAsync(new AskAssistantCommand(Personas.Get(persona), message), CancellationToken.None);
}
