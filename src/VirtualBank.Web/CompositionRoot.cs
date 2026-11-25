using VirtualBank.Application.Credit.Commands.RequestCredit;
using VirtualBank.Application.Customers.Commands.CashDesk;
using VirtualBank.Application.Customers.Commands.SelfService;
using VirtualBank.Application.Credit.Queries.ListCreditNotes;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Credit.Commands.ApproveCreditApplication;
using VirtualBank.Application.Assistant.Authorization;
using VirtualBank.Application.Assistant.Commands.AskAssistant;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Credit;
using VirtualBank.Application.Credit.Authorization;
using VirtualBank.Application.Credit.Commands.AddCreditNote;
using VirtualBank.Application.Credit.Queries.GetCreditApplication;
using VirtualBank.Application.Credit.Queries.ListCreditApplications;
using VirtualBank.Application.Customers;
using VirtualBank.Application.Customers.Authorization;
using VirtualBank.Application.Customers.Queries.GetCustomer;
using VirtualBank.Application.Customers.Queries.GetMyBanking;
using VirtualBank.Application.Customers.Queries.ListCustomerAccounts;
using VirtualBank.Application.Customers.Queries.ListCustomers;
using VirtualBank.Application.Customers.Queries.SummarizeCustomerBalances;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Documents.Authorization;
using VirtualBank.Application.Documents.Commands.IngestDocuments;
using VirtualBank.Infrastructure;
using VirtualBank.Presentation;

namespace VirtualBank.Web;

/// <summary>
/// Composition root: the only place that knows every layer. Lifetimes: policies, notice catalog, repositories and
/// data sources are stateless singletons (each repository call opens its own authorized transaction); readers and
/// handlers are scoped so they can take per-request collaborators later without becoming captive dependencies.
/// </summary>
public static class CompositionRoot
{
    public static IServiceCollection AddVirtualBank(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddProblemDetails();
        services.AddInfrastructure(configuration);
        services.AddApplication();
        return services;
    }

    /// <summary>Application layer registrations (the layer itself has no DI dependency).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IResourceAuthorizationService>(
            new ResourceAuthorizationService([.. CustomerPolicies.Create(), .. CreditPolicies.Create(), .. AssistantPolicies.Create(), .. DocumentPolicies.Create()]));
        services.AddSingleton(new NoticeCatalog([.. CreditPolicies.NoticeRules, .. AssistantPolicies.NoticeRules]));

        // Customers module
        services.AddScoped<CustomerReader>();
        services.AddScoped<ListCustomersHandler>();
        services.AddScoped<GetCustomerHandler>();
        services.AddScoped<ListCustomerAccountsHandler>();
        services.AddScoped<SummarizeCustomerBalancesHandler>();
        services.AddScoped<GetMyBankingHandler>();

        // Credit module
        services.AddScoped<CreditApplicationReader>();
        services.AddScoped<ListCreditApplicationsHandler>();
        services.AddScoped<GetCreditApplicationHandler>();
        services.AddScoped<AddCreditNoteHandler>();
        services.AddScoped<CreditApproval>();
        services.AddScoped<ListCreditNotesHandler>();
        services.AddScoped<MyPaymentOrders>();
        services.AddScoped<CashDesk>();
        services.AddScoped<CreditRequests>();
        services.AddScoped<IConfirmableAction, CreditRequestAction>();
        foreach (var kind in new[] { CashDesk.DepositKind, CashDesk.WithdrawalKind })
            services.AddScoped<IConfirmableAction>(sp => new CashDeskAction(sp.GetRequiredService<CashDesk>(), kind));
        // Confirmation cards: one handler per kind; ActionConfirmation dispatches and enforces owner + expiry.
        services.AddScoped<PendingActionFactory>();
        services.AddScoped<ActionConfirmation>();
        services.AddScoped<IConfirmableAction, CreditApproveAction>();
        services.AddScoped<IConfirmableAction, CreditRejectAction>();
        services.AddScoped<CreditNoteCards>();
        services.AddScoped<IConfirmableAction, CreditNoteAction>();
        foreach (var kind in new[] { MyPaymentOrders.TransferKind, MyPaymentOrders.InternalTransferKind, MyPaymentOrders.CreateStandingOrderKind,
                     MyPaymentOrders.CancelPaymentKind,
                     MyPaymentOrders.PauseStandingOrderKind, MyPaymentOrders.ResumeStandingOrderKind })
            services.AddScoped<IConfirmableAction>(sp => new MyPaymentOrderAction(sp.GetRequiredService<MyPaymentOrders>(), kind));

        // Assistant module: tools are adapters over the handlers above; the runtime port is bound in Infrastructure.
        services.AddSingleton<ToolPolicyGate>();
        services.AddScoped<AssistantToolCatalog>();
        services.AddScoped<AskAssistantHandler>();
        return services;
    }

    /// <summary>Middleware, health endpoints and business routes, in this order. Used by Program and the route tests.</summary>
    public static WebApplication UseVirtualBank(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHealthEndpoints();
        app.MapVirtualBankApi();
        return app;
    }

    /// <summary>
    /// Liveness has no dependencies. Readiness requires Customer, Credit and Document databases plus verifiable Keycloak signing keys; the
    /// response names only the failing dependency.
    /// </summary>
    private static void MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => TypedResults.Ok(new { status = "live" }));
        endpoints.MapGet("/health/ready", async Task<IResult> (ReadinessProbe probe, CancellationToken ct) =>
            await probe.FirstUnavailableAsync(ct) is { } failure
                ? TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: failure)
                : TypedResults.Ok(new { status = "ready" }));
    }
}
