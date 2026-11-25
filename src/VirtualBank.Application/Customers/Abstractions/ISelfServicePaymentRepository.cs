using VirtualBank.Application.Customers;
using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Application.Customers.Abstractions;

/// <summary>Subject-scoped, read-only payment data. PostgreSQL FORCE RLS verifies identity again.</summary>
public interface ISelfServicePaymentRepository
{
    Task<IReadOnlyList<ScheduledPaymentView>> UpcomingAsync(PrincipalContext principal, int days, CancellationToken ct);
    Task<IReadOnlyList<BookedTransactionView>> RecentAsync(PrincipalContext principal, int days, CancellationToken ct);
    Task<IReadOnlyList<StandingOrderView>> StandingOrdersAsync(PrincipalContext principal, CancellationToken ct);

    /// <summary>A planned (not yet executed) own payment due today or later; null if none is visible.</summary>
    Task<ScheduledPaymentView?> FindPlannedPaymentAsync(PrincipalContext principal, string paymentId, CancellationToken ct);

    /// <summary>Inserts a planned payment. False when RLS WITH CHECK refuses it (not own customer, no permission).</summary>
    Task<bool> CreatePlannedPaymentAsync(PrincipalContext principal, string customerId, string id, string description,
        decimal amount, string currency, DateOnly dueDate, CancellationToken ct);

    Task<bool> CancelPlannedPaymentAsync(PrincipalContext principal, string paymentId, CancellationToken ct);

    Task<bool> SetStandingOrderActiveAsync(PrincipalContext principal, string orderId, bool active, CancellationToken ct);

    /// <summary>Creates an active standing order. False when RLS WITH CHECK refuses it.</summary>
    Task<bool> CreateStandingOrderAsync(PrincipalContext principal, string customerId, string id, string description,
        decimal amount, string currency, string frequency, DateOnly nextDueDate, CancellationToken ct);
}
