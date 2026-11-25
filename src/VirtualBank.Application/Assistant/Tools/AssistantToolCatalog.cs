using VirtualBank.Application.Credit.Commands.RequestCredit;
using VirtualBank.Application.Customers.Commands.CashDesk;
using VirtualBank.Application.Customers.Commands.SelfService;
using VirtualBank.Application.Credit.Commands.ApproveCreditApplication;
using System.Collections.Frozen;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Credit.Commands.AddCreditNote;
using VirtualBank.Application.Credit.Queries.GetCreditApplication;
using VirtualBank.Application.Credit.Queries.ListCreditApplications;
using VirtualBank.Application.Customers.Queries.GetCustomer;
using VirtualBank.Application.Customers.Queries.ListCustomerAccounts;
using VirtualBank.Application.Customers.Queries.ListCustomers;
using VirtualBank.Application.Customers.Queries.SummarizeCustomerBalances;
using VirtualBank.Application.Customers.Queries.GetMyBanking;
using VirtualBank.Model.Credit;

namespace VirtualBank.Application.Assistant.Tools;

/// <summary>
/// The assistant's tools, each a thin adapter over an existing use case. Nothing here reads a repository directly:
/// the assistant can never see more than the same user could see through the HTTP API.
/// </summary>
public sealed class AssistantToolCatalog
{
    public const string CustomerIdPattern = "^C-[0-9]{4}$";
    public const string CreditApplicationIdPattern = "^K-[0-9]{3}$";

    private static readonly ToolParameter CustomerIdParameter =
        new("customer_id", "Customer identifier, e.g. C-1002.", ToolParameterKind.Identifier, CustomerIdPattern);

    private static readonly ToolParameter ApplicationIdParameter =
        new("application_id", "Credit application identifier, e.g. K-100.", ToolParameterKind.Identifier, CreditApplicationIdPattern);

    private readonly FrozenDictionary<string, AssistantTool> _tools;

    public AssistantToolCatalog(
        ListCustomersHandler listCustomers,
        GetCustomerHandler getCustomer,
        ListCustomerAccountsHandler listAccounts,
        ListCreditApplicationsHandler listApplications,
        GetCreditApplicationHandler getApplication,
        AddCreditNoteHandler addNote,
        ISecureDocumentSearch? documentSearch = null,
        SummarizeCustomerBalancesHandler? summarizeBalances = null,
        GetMyBankingHandler? myBanking = null,
        SecureDocumentExcerpt? documentExcerpt = null,
        SecureDocumentCatalog? documentCatalog = null,
        CreditApproval? creditApproval = null,
        CreditNoteCards? noteCards = null,
        MyPaymentOrders? paymentOrders = null,
        CashDesk? cashDesk = null,
        CreditRequests? creditRequests = null)
    {
        Permission[] customerRead = [KnownPermissions.CustomerRead, KnownPermissions.CustomerReadAssigned];
        Permission[] creditRead = [KnownPermissions.CreditRead, KnownPermissions.CreditApprove, KnownPermissions.CreditRequest];

        AssistantTool[] tools =
        [
            new(new("list_customers", "Lists ONLY customer IDs, names and branches visible to this user; does NOT return account balances or money totals.", [], ToolRisk.Read), customerRead,
                async (p, _, ct) => ToolResults.From(await listCustomers.HandleAsync(new ListCustomersQuery(p), ct))),

            new(new("get_customer", "Returns one customer. Sensitive fields are masked unless the user may see them.",
                    [CustomerIdParameter], ToolRisk.Read), customerRead,
                async (p, a, ct) => ToolResults.From(await getCustomer.HandleAsync(new GetCustomerQuery(p, a.Get("customer_id")), ct))),

            new(new("list_customer_accounts", "Lists the account balances of one specific customer ID. For totals across accessible customers use summarize_customer_balances.", [CustomerIdParameter], ToolRisk.Read), customerRead,
                async (p, a, ct) => ToolResults.From(
                    await listAccounts.HandleAsync(new ListCustomerAccountsQuery(p, a.Get("customer_id")), ct))),

            new(new("list_credit_applications", "Lists the credit applications the user may see.", [], ToolRisk.Read), creditRead,
                async (p, _, ct) => ToolResults.From(await listApplications.HandleAsync(new ListCreditApplicationsQuery(p), ct))),

            new(new("get_credit_application", "Returns one credit application.", [ApplicationIdParameter], ToolRisk.Read), creditRead,
                async (p, a, ct) => ToolResults.From(
                    await getApplication.HandleAsync(new GetCreditApplicationQuery(p, a.Get("application_id")), ct))),

            // Writes are never executed from a model turn. With the confirmation services wired, these tools return a
            // card the user confirms (or discards) in the chat; without them they are refused by the gate as before.
            noteCards is null
                ? new(new("add_credit_note", "Adds an internal note to a credit application.",
                        [ApplicationIdParameter, NoteText], ToolRisk.LowRiskWrite), [KnownPermissions.CreditNoteWrite],
                    async (p, a, ct) => ToolResults.From(
                        await addNote.HandleAsync(new AddCreditNoteCommand(p, a.Get("application_id"), a.Get("text")), ct),
                        note => new { noteId = note.Id, applicationId = note.ApplicationId.Value, createdAt = note.CreatedAt }))
                : new(new("add_credit_note", "PREPARES an internal note on a credit application as a confirmation card. " +
                        "Nothing is saved until the user clicks Confirm on the card.", [ApplicationIdParameter, NoteText],
                        ToolRisk.PreparesConfirmation), [KnownPermissions.CreditNoteWrite],
                    async (p, a, ct) => ToolResults.From(await noteCards.PrepareAsync(p, a.Get("application_id"), a.Get("text"), ct))),

            creditApproval is null
                ? new(new("approve_credit_application", "Requests approval of a credit application. Requires separate human confirmation.",
                        [ApplicationIdParameter], ToolRisk.RequiresHumanConfirmation), [KnownPermissions.CreditApprove],
                    (_, _, _) => Task.FromResult(ToolOutcome.Denied(AssistantNoticeCodes.HumanConfirmationRequired)))
                : new(new("approve_credit_application", "PREPARES the approval of a SUBMITTED credit application as a confirmation " +
                        "card (checks state, the user's limit and four-eyes). Nothing is approved until the user confirms the card.",
                        [ApplicationIdParameter, DecisionNote(required: false)], ToolRisk.PreparesConfirmation), [KnownPermissions.CreditApprove],
                    async (p, a, ct) => ToolResults.From(await creditApproval.PrepareApprovalAsync(p, a.Get("application_id"), a.Find("note"), ct))),
        ];

        if (creditApproval is not null)
        {
            tools = [.. tools,
                new(new("reject_credit_application", "PREPARES the rejection of a SUBMITTED credit application as a confirmation " +
                        "card. A reason is required. Nothing changes until the user confirms the card.",
                        [ApplicationIdParameter, DecisionNote(required: true)], ToolRisk.PreparesConfirmation), [KnownPermissions.CreditApprove],
                    async (p, a, ct) => ToolResults.From(await creditApproval.PrepareRejectionAsync(p, a.Get("application_id"), a.Find("note"), ct)))];
        }

        if (paymentOrders is not null)
        {
            Permission[] orders = [KnownPermissions.CustomerPaymentWrite];
            tools = [.. tools,
                new(new("plan_transfer", "PREPARES a SEPA transfer from the logged-in customer's OWN account as a confirmation card " +
                        "(beneficiary, IBAN, amount, currency, optional date yyyy-MM-dd and reference). Nothing is sent until the customer " +
                        "confirms the card. Ask for missing beneficiary, IBAN or amount instead of guessing. No customer_id input.",
                        [new("beneficiary_name", "Beneficiary name.", ToolParameterKind.Text, MaxLength: 70),
                         new("iban", "Beneficiary IBAN.", ToolParameterKind.Text, MaxLength: 42),
                         new("amount", "Amount, e.g. 120.50.", ToolParameterKind.Text, MaxLength: 12),
                         new("currency", "ISO currency of one of the customer's accounts, e.g. EUR.", ToolParameterKind.Text, MaxLength: 3),
                         new("execution_date", "yyyy-MM-dd; omit for today.", ToolParameterKind.Text, Required: false, MaxLength: 10),
                         new("reference", "Payment reference.", ToolParameterKind.Text, Required: false, MaxLength: 140)],
                        ToolRisk.PreparesConfirmation), orders,
                    async (p, a, ct) => ToolResults.From(await paymentOrders.PrepareTransferAsync(p, new TransferRequest(
                        a.Get("beneficiary_name"), a.Get("iban"), ParseAmount(a.Get("amount")), a.Get("currency"),
                        ParseDate(a.Find("execution_date")), a.Find("reference")), ct))),
                new(new("cancel_planned_payment", "PREPARES cancelling one of the customer's OWN planned (future) payments by its ID " +
                        "(e.g. P-2001-01) as a confirmation card. Use get_my_upcoming_payments first to find the ID.",
                        [new("payment_id", "Planned payment ID, e.g. P-2001-01.", ToolParameterKind.Identifier, "^P-[0-9]{4}-[A-Z0-9]{2,8}$")],
                        ToolRisk.PreparesConfirmation), orders,
                    async (p, a, ct) => ToolResults.From(await paymentOrders.PrepareCancelPaymentAsync(p, a.Get("payment_id"), ct))),
                new(new("change_standing_order", "PREPARES pausing or resuming one of the customer's OWN standing orders by its ID " +
                        "(e.g. SO-2001-01) as a confirmation card. action is 'pause' or 'resume'.",
                        [new("standing_order_id", "Standing order ID, e.g. SO-2001-01.", ToolParameterKind.Identifier, "^SO-[0-9]{4}-[A-Z0-9]{2,8}$"),
                         new("action", "'pause' or 'resume'.", ToolParameterKind.Identifier, "^(pause|resume)$")],
                        ToolRisk.PreparesConfirmation), orders,
                    async (p, a, ct) => ToolResults.From(await paymentOrders.PrepareStandingOrderAsync(p, a.Get("standing_order_id"),
                        a.Get("action") == "resume", ct))),
                new(new("transfer_between_my_accounts", "PREPARES a transfer between two of the logged-in customer's OWN accounts " +
                        "(same currency, booked immediately after confirmation) as a confirmation card. Use get_my_accounts for the IDs.",
                        [new("from_account_id", "Source account ID, e.g. A-20011.", ToolParameterKind.Identifier, AccountIdPattern),
                         new("to_account_id", "Target account ID, e.g. A-20012.", ToolParameterKind.Identifier, AccountIdPattern),
                         new("amount", "Amount, e.g. 250.", ToolParameterKind.Text, MaxLength: 12)],
                        ToolRisk.PreparesConfirmation), orders,
                    async (p, a, ct) => ToolResults.From(await paymentOrders.PrepareInternalTransferAsync(p, a.Get("from_account_id"),
                        a.Get("to_account_id"), ParseAmount(a.Get("amount")), ct))),
                new(new("create_standing_order", "PREPARES a NEW standing order (Dauerauftrag / otomatik ödeme talimatı) from the " +
                        "customer's OWN account as a confirmation card: beneficiary, IBAN, amount, currency, frequency weekly|monthly, " +
                        "optional first date yyyy-MM-dd and reference. Ask for missing details instead of guessing.",
                        [new("beneficiary_name", "Beneficiary name.", ToolParameterKind.Text, MaxLength: 70),
                         new("iban", "Beneficiary IBAN.", ToolParameterKind.Text, MaxLength: 42),
                         new("amount", "Amount per execution.", ToolParameterKind.Text, MaxLength: 12),
                         new("currency", "ISO currency of one of the customer's accounts.", ToolParameterKind.Text, MaxLength: 3),
                         new("frequency", "'weekly' or 'monthly'.", ToolParameterKind.Identifier, "^(weekly|monthly)$"),
                         new("first_date", "yyyy-MM-dd; omit for tomorrow.", ToolParameterKind.Text, Required: false, MaxLength: 10),
                         new("reference", "Payment reference.", ToolParameterKind.Text, Required: false, MaxLength: 140)],
                        ToolRisk.PreparesConfirmation), orders,
                    async (p, a, ct) => ToolResults.From(await paymentOrders.PrepareCreateStandingOrderAsync(p, new StandingOrderRequest(
                        a.Get("beneficiary_name"), a.Get("iban"), ParseAmount(a.Get("amount")), a.Get("currency"), a.Get("frequency"),
                        ParseDate(a.Find("first_date")), a.Find("reference")), ct)))];
        }

        if (creditRequests is not null)
        {
            tools = [.. tools,
                new(new("request_credit", "PREPARES a credit application by the logged-in bank customer as a confirmation card: amount in " +
                        "EUR (500-250,000), term in months (6-360) and purpose. After confirmation it is SUBMITTED for an approver's decision. " +
                        "Ask for missing amount, term or purpose instead of guessing.",
                        [new("amount", "Amount in EUR.", ToolParameterKind.Text, MaxLength: 12),
                         new("term_months", "Term in months, 6-360.", ToolParameterKind.Text, MaxLength: 3),
                         new("purpose", "Purpose of the credit.", ToolParameterKind.Text, MaxLength: 160)],
                        ToolRisk.PreparesConfirmation), [KnownPermissions.CreditRequest],
                    async (p, a, ct) => ToolResults.From(await creditRequests.PrepareAsync(p, ParseAmount(a.Get("amount")),
                        int.TryParse(a.Get("term_months"), out var months) ? months : -1, a.Get("purpose"), ct)))];
        }

        if (cashDesk is not null)
        {
            Permission[] cash = [KnownPermissions.AccountCash];
            ToolParameter[] cashParameters =
            [
                CustomerIdParameter,
                new("account_id", "Account ID of that customer, e.g. A-10021.", ToolParameterKind.Identifier, AccountIdPattern),
                new("amount", "Cash amount, up to 10,000.", ToolParameterKind.Text, MaxLength: 12),
            ];
            tools = [.. tools,
                new(new("cash_deposit", "PREPARES a cash DEPOSIT at the branch counter on an account of a customer of the advisor's OWN " +
                        "branch as a confirmation card (customer_id, account_id, amount). Use list_customer_accounts for account IDs.",
                        cashParameters, ToolRisk.PreparesConfirmation), cash,
                    async (p, a, ct) => ToolResults.From(await cashDesk.PrepareAsync(p, a.Get("customer_id"), a.Get("account_id"),
                        ParseAmount(a.Get("amount")), deposit: true, ct))),
                new(new("cash_withdrawal", "PREPARES a cash WITHDRAWAL at the branch counter from an account of a customer of the advisor's " +
                        "OWN branch as a confirmation card. Cannot overdraw the account.",
                        cashParameters, ToolRisk.PreparesConfirmation), cash,
                    async (p, a, ct) => ToolResults.From(await cashDesk.PrepareAsync(p, a.Get("customer_id"), a.Get("account_id"),
                        ParseAmount(a.Get("amount")), deposit: false, ct)))];
        }

        if (summarizeBalances is not null)
        {
            tools = [.. tools,
                new(new("summarize_customer_balances",
                        "Read-only, authorized account-balance totals PER CURRENCY for all accessible customers or one selected customer. " +
                        "Use for balance, money, funds, cash, bakiye, nakit, para, Kontostand, Guthaben, Gesamtbetrag questions. " +
                        "The values are ACCOUNT BALANCES, not physical cash or net worth. " +
                        "Optionally provide customer_id OR customer_name; omit both to summarize all customers visible to the caller. " +
                        "NEVER sum different currencies; the existing Customer use cases enforce authorization and RLS.",
                        [new("customer_id", "Optional customer ID (C-1002). Leave blank for all accessible customers or when providing customer_name.",
                             ToolParameterKind.Identifier, CustomerIdPattern, Required: false),
                         new("customer_name", "Optional exact/uniquely matching name of a visible customer, e.g. Jana Weidner. " +
                             "Do not supply together with customer_id.", ToolParameterKind.Text, Required: false, MaxLength: 100)],
                        ToolRisk.Read), customerRead,
                    async (principal, args, ct) => ToolResults.From(await summarizeBalances.HandleAsync(
                        new SummarizeCustomerBalancesQuery(principal, args.Find("customer_id"), args.Find("customer_name")), ct)))];
        }

        if (documentSearch is not null)
        {
            tools = [.. tools,
                new(new("search_documents", "Search authorized policy documents using multilingual semantic retrieval. The result contains safe citations.",
                        [new("query", "Natural-language policy question (English, German or Turkish).", ToolParameterKind.Text, MaxLength: 500)],
                        ToolRisk.Read), [KnownPermissions.DocumentRead],
                    async (p, a, ct) => ToolResults.From(
                        await documentSearch.SearchAsync(p, a.Get("query"), 5, ct)))];
        }

        if (documentExcerpt is not null)
        {
            tools = [.. tools,
                new(new("get_document_excerpt", "Fetch up to six authorized excerpts from a specific document ID DOC-101, DOC-106 etc. Use only for 'open/show DOC-###' or 'dosyayı göster' with a precise ID. Not an original-file download. Document RLS and ACL are rechecked.",
                    [new("document_id", "Exact document identifier like DOC-101.", ToolParameterKind.Identifier, "^DOC-[0-9]{3}$")], ToolRisk.Read),
                    [KnownPermissions.DocumentRead],
                    async (principal, args, ct) => ToolResults.From(await documentExcerpt.ReadAsync(principal, args.Get("document_id"), ct)))];
        }

        if (documentCatalog is not null)
        {
            tools = [.. tools,
                new(new("list_documents", "Lists the IDs, titles, language and classification of every document this user may read. " +
                        "Use for 'which documents/policies can I access', 'belgelerimi listele', 'welche Dokumente'. " +
                        "Returns metadata only, no content: for questions ABOUT a policy use search_documents.", [], ToolRisk.Read),
                    [KnownPermissions.DocumentRead],
                    async (principal, _, ct) => ToolResults.From(await documentCatalog.ListAsync(principal, ct)))];
        }

        // Self-service tools NEVER accept a customer ID: authoritative owner mapping uses JWT sub.
        // No tool exposes customer/credit data to admin or to a different bank customer.
        tools = [.. tools,
            new(new("describe_my_access", "Explain ONLY the current caller's granted permissions and available capabilities. Never claim administrator access to customer data.", [], ToolRisk.Read),
                [KnownPermissions.AssistantUse],
                (principal, _, _) => Task.FromResult(new ToolOutcome(ToolCallStatus.Succeeded, new
                {
                    roles = principal.Roles.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    permissions = principal.Permissions.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    note = "Permissions describe capabilities, not blanket resource visibility. No bank data was fetched."
                })))];
        if (myBanking is not null)
        {
            Permission[] personal = [KnownPermissions.CustomerReadSelf];
            tools = [.. tools,
                new(new("get_my_profile", "Returns ONLY the logged-in bank customer's OWN profile, including their IBAN when authorized. For 'my IBAN', 'meine IBAN', 'IBAN'ım'. No customer_id input.", [], ToolRisk.Read),personal,
                    async (principal, _, ct) => ToolResults.From(await myBanking.ProfileAsync(principal, ct))),
                new(new("get_my_accounts", "Returns ONLY the logged-in bank customer's OWN accounts and balances. No customer_id input.", [], ToolRisk.Read), personal,
                    async (principal, _, ct) => ToolResults.From(await myBanking.AccountsAsync(principal, ct))),
                new(new("get_my_balance_summary", "SUMMARIZE the logged-in bank customer's OWN ACCOUNT BALANCES by currency, for 'my money', 'bakiyem', 'mein Kontostand'. Not cash or net worth. No customer_id input.", [], ToolRisk.Read), personal,
                    async (principal, _, ct) => ToolResults.From(await myBanking.BalanceAsync(principal, ct))),
                new(new("get_my_upcoming_payments", "List ONLY the current bank customer's scheduled future payments within the next N calendar days (3, 5, 30 etc). Not past transactions. days default 30, valid 1-90. No customer_id.",
                        [new("days", "Number of days into the future, 1-90; omit for 30.", ToolParameterKind.Text, Required:false, MaxLength:2)], ToolRisk.Read), personal,
                    async (principal, args, ct) => ToolResults.From(await myBanking.UpcomingAsync(principal, ParseDays(args), ct))),
                new(new("get_my_recent_transactions", "List the logged-in bank customer's booked transactions over the PAST N days (3,5,30). Distinct from future payments. days default 30, valid 1-90.",
                        [new("days", "Days into the past, 1-90; omit for 30.",ToolParameterKind.Text,Required:false,MaxLength:2)], ToolRisk.Read),personal,
                    async (principal, args, ct) => ToolResults.From(await myBanking.RecentAsync(principal, ParseDays(args), ct))),
                new(new("get_my_standing_orders", "List ONLY the logged-in bank customer's active automatic standing orders / Daueraufträge / otomatik ödemeler. Read-only.", [], ToolRisk.Read),personal,
                    async (principal, _, ct) => ToolResults.From(await myBanking.StandingOrdersAsync(principal, ct)))];
        }

        _tools = tools.ToFrozenDictionary(t => t.Name, StringComparer.Ordinal);
        Definitions = tools.Select(t => t.Definition).ToList();
    }

    /// <summary>All tool schemas, in a stable order. Schemas are not secrets; authorization happens at invocation.</summary>
    public IReadOnlyList<ToolDefinition> Definitions { get; }

    /// <summary>
    /// Hide mode: models see only read-only tools for which the principal has a general capability.
    /// Explain mode: may advertise missing read capabilities for policy-approved generic hints.
    /// In both modes execution always goes through ToolPolicyGate and the resource use case.
    /// Mutating and approval tools are never model-visible until an independent HITL protocol exists.
    /// </summary>
    public IReadOnlyList<ToolDefinition> VisibleDefinitions(PrincipalContext principal, ToolVisibilityMode mode = ToolVisibilityMode.Hide) =>
        _tools.Values.Where(t =>
                (t.Definition.Risk == ToolRisk.Read && (mode == ToolVisibilityMode.Explain || t.RequiresAnyOf.Any(principal.HasPermission))) ||
                // Card-preparing tools are offered only to principals who could confirm the card themselves.
                (t.Definition.Risk == ToolRisk.PreparesConfirmation && t.RequiresAnyOf.Any(principal.HasPermission)))
            .Select(t => t.Definition).OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();

    private const string AccountIdPattern = "^A-[0-9]{5}$";

    private static readonly ToolParameter NoteText =
        new("text", "Note text.", ToolParameterKind.Text, MaxLength: CreditNoteText.MaxLength);

    private static ToolParameter DecisionNote(bool required) =>
        new("note", required ? "Reason for the decision (required)." : "Optional comment for the decision.",
            ToolParameterKind.Text, Required: required, MaxLength: CreditApproval.MaxNoteLength);

    private static decimal ParseAmount(string raw) =>
        decimal.TryParse(raw.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount)
            ? amount : -1m;

    private static DateOnly? ParseDate(string? raw) =>
        raw is not null && DateOnly.TryParseExact(raw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) ? date : null;

    private static int ParseDays(ToolArguments args) =>
        args.Find("days") is { } raw && int.TryParse(raw, out var days) ? days :
        args.Find("days") is null ? 30 : -1;

    public AssistantTool? Find(string name) => _tools.GetValueOrDefault(name);
}
