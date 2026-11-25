using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Model.Credit;
using VirtualBank.Application.Credit;

namespace VirtualBank.Application.Credit.Abstractions;

/// <summary>
/// Port to credit persistence. Every call runs under the caller's database authorization context (RLS),
/// so the principal is part of the contract. The repository never makes authorization decisions.
/// </summary>
public interface ICreditApplicationRepository
{
    Task<IReadOnlyList<CreditApplication>> ListAsync(PrincipalContext principal, CancellationToken cancellationToken);

    Task<CreditApplication?> FindAsync(PrincipalContext principal, CreditApplicationId applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a note authored by the principal. Returns null when the database rejects the row
    /// (RLS WITH CHECK), e.g. because the assignment changed after the use case's policy check.
    /// </summary>
    Task<CreditNote?> AddNoteAsync(PrincipalContext principal, CreditApplicationId applicationId, CreditNoteText text, CancellationToken cancellationToken);

    /// <summary>Notes of one application, oldest first, as filtered by RLS (notes_read).</summary>
    /// <summary>
    /// Creates a SUBMITTED application prepared by the caller (customer credit request). Returns the new ID, or null
    /// when the database refuses it (RLS WITH CHECK: credit.request, preparer = caller, bounds).
    /// </summary>
    Task<string?> CreateRequestAsync(PrincipalContext principal, string customerId, decimal amount, string currency,
        string purpose, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredCreditNote>> ListNotesAsync(PrincipalContext principal, CreditApplicationId applicationId, CancellationToken cancellationToken);
}
