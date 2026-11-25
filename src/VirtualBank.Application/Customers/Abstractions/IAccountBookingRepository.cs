using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Customers;

namespace VirtualBank.Application.Customers.Abstractions;

/// <summary>
/// The only way a balance changes. All lines are booked in ONE database transaction: each line updates one account
/// balance and records a transaction; if any line fails (not visible under RLS, or the balance would go below zero)
/// nothing is booked. Which accounts may be booked is decided by RLS (own accounts vs. branch cash desk).
/// </summary>
public interface IAccountBookingRepository
{
    Task<BookingResult> BookAsync(PrincipalContext principal, string customerId, IReadOnlyList<BookingLine> lines,
        CancellationToken cancellationToken, BeneficiaryCredit? beneficiary = null);
}
