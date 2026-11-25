using VirtualBank.Application.Common;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Credit;

namespace VirtualBank.Application.Credit.Queries.GetCreditApplication;

public sealed record GetCreditApplicationQuery(PrincipalContext Principal, string ApplicationId);

/// <summary>Query: one application, or NotFound when it is not visible to the caller.</summary>
public sealed class GetCreditApplicationHandler(CreditApplicationReader reader, NoticeCatalog notices)
{
    public async Task<UseCaseResult<CreditApplicationView>> HandleAsync(GetCreditApplicationQuery query, CancellationToken cancellationToken)
    {
        if (!CreditApplicationReader.HasAnyReadPath(query.Principal))
        {
            return UseCaseResult<CreditApplicationView>.Forbidden(
                notices.HintCodeFor(KnownPermissions.CreditRead, ReasonCodes.MissingPermission));
        }

        var application = await reader.FindReadableAsync(query.Principal, query.ApplicationId, cancellationToken);
        return application is null
            ? UseCaseResult<CreditApplicationView>.NotFound()
            : UseCaseResult<CreditApplicationView>.Success(CreditApplicationView.From(application));
    }
}
