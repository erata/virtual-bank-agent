using System.Globalization;
using System.Text.RegularExpressions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Credit.Abstractions;

namespace VirtualBank.Application.Credit.Commands.RequestCredit;

/// <summary>
/// A bank customer applies for a credit. The application is created SUBMITTED and prepared by the customer, so the
/// usual approval workflow (approver within limit, four-eyes) decides it. The customer ID comes from the customer
/// module for THIS token subject (never from input), and the database again checks credit.request and preparer.
/// </summary>
public sealed class CreditRequests(ICreditApplicantDirectory applicants, ICreditApplicationRepository repository,
    PendingActionFactory actions)
{
    public const string Kind = "credit.request";
    public const decimal MinAmount = 500m;
    public const decimal MaxAmount = 250_000m;

    private static readonly Regex Purpose = new(@"^[\p{L}\p{M}0-9 .,:;'()/&+-]{3,160}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public async Task<UseCaseResult<PendingActionView>> PrepareAsync(PrincipalContext principal, decimal amount, int termMonths,
        string? purpose, CancellationToken ct)
    {
        if (!principal.HasPermission(KnownPermissions.CreditRequest) || principal.Type == PrincipalType.Machine)
            return UseCaseResult<PendingActionView>.Forbidden(null);
        var text = purpose?.Trim() ?? "";
        if (amount < MinAmount || amount > MaxAmount || decimal.Round(amount, 2) != amount)
            return UseCaseResult<PendingActionView>.Invalid("amount", $"Enter an amount between {MinAmount:N0} and {MaxAmount:N0} EUR.");
        if (termMonths is < 6 or > 360) return UseCaseResult<PendingActionView>.Invalid("termMonths", "Choose a term between 6 and 360 months.");
        if (!Purpose.IsMatch(text)) return UseCaseResult<PendingActionView>.Invalid("purpose", "Describe the purpose in 3-160 plain characters.");
        var customerId = await applicants.FindOwnCustomerIdAsync(principal, ct);
        if (customerId is null) return UseCaseResult<PendingActionView>.NotFound();

        var fullPurpose = $"{text} ({termMonths} months)";
        var action = actions.Create(principal, Kind,
            new Dictionary<string, string>
            {
                ["customerId"] = customerId,
                ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
                ["purpose"] = fullPurpose,
            },
            [
                new("customer", customerId),
                new("amount", amount.ToString("N2", CultureInfo.InvariantCulture) + " EUR"),
                new("term", termMonths.ToString(CultureInfo.InvariantCulture)),
                new("purpose", text),
            ]);
        return UseCaseResult<PendingActionView>.Success(PendingActionView.From(action));
    }

    internal async Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action, CancellationToken ct)
    {
        if (!principal.HasPermission(KnownPermissions.CreditRequest)) return UseCaseResult<ActionOutcome>.Forbidden(null);
        // The customer row must still belong to this token subject.
        if (await applicants.FindOwnCustomerIdAsync(principal, ct) != action.Arguments["customerId"]) return UseCaseResult<ActionOutcome>.NotFound();
        var id = await repository.CreateRequestAsync(principal, action.Arguments["customerId"],
            decimal.Parse(action.Arguments["amount"], CultureInfo.InvariantCulture), "EUR", action.Arguments["purpose"], ct);
        return id is null
            ? UseCaseResult<ActionOutcome>.Forbidden(null)
            : UseCaseResult<ActionOutcome>.Success(new ActionOutcome(Kind, "SUBMITTED", id, [new("application", id)]));
    }
}

/// <summary>Confirms "credit.request" cards.</summary>
public sealed class CreditRequestAction(CreditRequests requests) : IConfirmableAction
{
    public string Kind => CreditRequests.Kind;

    public Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action, CancellationToken ct) =>
        requests.ExecuteAsync(principal, action, ct);
}
