namespace VirtualBank.Application.Customers;

public sealed record ScheduledPaymentView(string Id, string CustomerId, string Description, decimal Amount, string Currency, DateOnly DueDate, string Status);
public sealed record BookedTransactionView(string Id, string CustomerId, string Description, decimal Amount, string Currency, DateOnly BookedOn);
public sealed record StandingOrderView(string Id, string CustomerId, string Description, decimal Amount, string Currency, string Frequency, DateOnly NextDueDate, bool Active);

