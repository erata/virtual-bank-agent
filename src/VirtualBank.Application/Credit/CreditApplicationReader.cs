using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Credit.Abstractions;
using VirtualBank.Application.Credit.Authorization;
using VirtualBank.Model.Credit;

namespace VirtualBank.Application.Credit;

/// <summary>
/// Loads applications under RLS and re-authorizes every one with the application policy (defense in depth).
/// A row RLS returned but the policy denies is treated as invisible and audited as a mismatch.
/// Shared by the queries and commands so the rule exists in one place.
/// </summary>
public sealed class CreditApplicationReader(
    ICreditApplicationRepository repository, IResourceAuthorizationService authorization, IAuthorizationAudit audit)
{
    public static bool HasAnyReadPath(PrincipalContext principal) =>
        principal.HasPermission(KnownPermissions.CreditRead) || principal.HasPermission(KnownPermissions.CreditApprove) ||
        principal.HasPermission(KnownPermissions.CreditRequest);

    public async Task<IReadOnlyList<CreditApplication>> ListReadableAsync(PrincipalContext principal, CancellationToken cancellationToken)
    {
        var readable = new List<CreditApplication>();
        foreach (var application in await repository.ListAsync(principal, cancellationToken))
        {
            if (await IsReadableAsync(principal, application, cancellationToken))
            {
                readable.Add(application);
            }
        }

        return readable;
    }

    public async Task<CreditApplication?> FindReadableAsync(PrincipalContext principal, string applicationId, CancellationToken cancellationToken)
    {
        // Malformed ids are indistinguishable from invisible ones and never reach the database.
        if (!CreditApplicationId.TryParse(applicationId, out var id))
        {
            return null;
        }

        var application = await repository.FindAsync(principal, id, cancellationToken);
        return application is not null && await IsReadableAsync(principal, application, cancellationToken) ? application : null;
    }

    private async Task<bool> IsReadableAsync(PrincipalContext principal, CreditApplication application, CancellationToken cancellationToken)
    {
        var decision = await authorization.AuthorizeAsync(
            principal, new CreditApplicationResource(application), KnownPermissions.CreditRead, cancellationToken);
        if (decision.IsAllowed)
        {
            audit.Record(principal, decision);
            return true;
        }

        audit.RecordPolicyRlsMismatch(principal, decision);
        return false;
    }
}
