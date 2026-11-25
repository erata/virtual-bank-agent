using VirtualBank.Application.Common;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Credit;

namespace VirtualBank.Application.Credit.Queries.ListCreditApplications;

public sealed record ListCreditApplicationsQuery(PrincipalContext Principal);

/// <summary>Query: the applications the caller may read.</summary>
public sealed class ListCreditApplicationsHandler(CreditApplicationReader reader, NoticeCatalog notices)
{
    public async Task<UseCaseResult<IReadOnlyList<CreditApplicationView>>> HandleAsync(
        ListCreditApplicationsQuery query, CancellationToken cancellationToken)
    {
        if (!CreditApplicationReader.HasAnyReadPath(query.Principal))
        {
            return UseCaseResult<IReadOnlyList<CreditApplicationView>>.Forbidden(
                notices.HintCodeFor(KnownPermissions.CreditRead, ReasonCodes.MissingPermission));
        }

        var applications = await reader.ListReadableAsync(query.Principal, cancellationToken);
        return UseCaseResult<IReadOnlyList<CreditApplicationView>>.Success(applications.Select(CreditApplicationView.From).ToList());
    }
}
