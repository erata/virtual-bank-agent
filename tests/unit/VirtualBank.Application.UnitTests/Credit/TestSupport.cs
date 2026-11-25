using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Credit.Abstractions;
using VirtualBank.Application.Credit.Authorization;
using VirtualBank.Application.Credit.Commands.AddCreditNote;
using VirtualBank.Application.Common;
using VirtualBank.Application.Credit;
using VirtualBank.Application.Credit.Queries.GetCreditApplication;
using VirtualBank.Application.Credit.Queries.ListCreditApplications;
using VirtualBank.Model.Credit;
using VirtualBank.Application.UnitTests.Support;

namespace VirtualBank.Application.UnitTests.Credit;

/// <summary>Ignores the principal, like a database without working RLS.</summary>
internal sealed class UnfilteredRepository(params CreditApplication[] seed) : ICreditApplicationRepository
{
    private readonly List<CreditApplication> applications = [.. seed];

    /// <summary>Creates SUBMITTED requests prepared by the caller, like the database (K-300, K-301 ...).</summary>
    public Task<string?> CreateRequestAsync(PrincipalContext principal, string customerId, decimal amount, string currency,
        string purpose, CancellationToken cancellationToken)
    {
        var id = $"K-{300 + applications.Count(a => a.Id.Value.StartsWith("K-3", StringComparison.Ordinal))}";
        applications.Add(CreditApplication.Rehydrate(CreditApplicationId.Parse(id), customerId, Money.Of(amount, currency),
            CreditApplicationStatus.Submitted, purpose, principal.SubjectId, null));
        return Task.FromResult<string?>(id);
    }

    public List<string> InsertedNotes { get; } = [];

    public int Reads { get; private set; }

    public bool RejectInsert { get; init; }

    public Task<IReadOnlyList<CreditApplication>> ListAsync(PrincipalContext principal, CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<CreditApplication>>(applications.ToArray());
    }

    public Task<CreditApplication?> FindAsync(PrincipalContext principal, CreditApplicationId applicationId, CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(applications.FirstOrDefault(a => a.Id == applicationId));
    }

    public Task<IReadOnlyList<StoredCreditNote>> ListNotesAsync(PrincipalContext principal, CreditApplicationId applicationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredCreditNote>>(InsertedNotes.Select((body, i) => new StoredCreditNote(i + 1, principal.SubjectId, body, DateTimeOffset.UnixEpoch)).ToArray());

    public Task<CreditNote?> AddNoteAsync(PrincipalContext principal, CreditApplicationId applicationId, CreditNoteText text, CancellationToken cancellationToken)
    {
        if (RejectInsert)
        {
            return Task.FromResult<CreditNote?>(null);
        }

        InsertedNotes.Add(text.Value);
        return Task.FromResult<CreditNote?>(new CreditNote(1, applicationId, principal.SubjectId, DateTimeOffset.UnixEpoch));
    }
}

/// <summary>Wires the use cases exactly like the composition root, but with test doubles for the ports.</summary>
internal sealed class CreditUseCases
{
    public CreditUseCases(params CreditApplication[] applications)
    {
        Repository = new UnfilteredRepository(applications);
    }

    public UnfilteredRepository Repository { get; init; }

    public RecordingAudit Audit { get; } = new();

    public static ResourceAuthorizationService PolicyEngine { get; } = new ResourceAuthorizationService(CreditPolicies.Create());

    public static NoticeCatalog Notices { get; } = new(CreditPolicies.NoticeRules);

    private CreditApplicationReader Reader => new(Repository, PolicyEngine, Audit);

    public ListCreditApplicationsHandler List => new(Reader, Notices);

    public GetCreditApplicationHandler Get => new(Reader, Notices);

    public AddCreditNoteHandler AddNote => new(Reader, Repository, PolicyEngine, Audit, Notices);

    public static CreditApplication NewApplication(string id, CreditApplicationStatus status, string? analyst, string preparedBy = "someone") =>
        CreditApplication.Rehydrate(CreditApplicationId.Parse(id), "C-1002", Money.Eur(1000m), status, "Test", preparedBy, analyst);
}
