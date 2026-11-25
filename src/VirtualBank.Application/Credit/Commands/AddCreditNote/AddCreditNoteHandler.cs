using VirtualBank.Application.Common;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Credit.Abstractions;
using VirtualBank.Application.Credit.Authorization;
using VirtualBank.Application.Credit;
using VirtualBank.Model.Credit;

namespace VirtualBank.Application.Credit.Commands.AddCreditNote;

public sealed record AddCreditNoteCommand(PrincipalContext Principal, string ApplicationId, string? Text);

/// <summary>
/// Command: add a note. Order: permission → input → visibility (RLS + policy) → resource policy → write.
/// The write is re-checked by the database (RLS WITH CHECK), closing the gap between check and write.
/// </summary>
public sealed class AddCreditNoteHandler(
    CreditApplicationReader reader, ICreditApplicationRepository repository, IResourceAuthorizationService authorization,
    IAuthorizationAudit audit, NoticeCatalog notices)
{
    public async Task<UseCaseResult<CreditNote>> HandleAsync(AddCreditNoteCommand command, CancellationToken cancellationToken)
    {
        if (!command.Principal.HasPermission(KnownPermissions.CreditNoteWrite))
        {
            return UseCaseResult<CreditNote>.Forbidden(notices.HintCodeFor(KnownPermissions.CreditNoteWrite, ReasonCodes.MissingPermission));
        }

        if (!CreditNoteText.TryCreate(command.Text, out var text))
        {
            return UseCaseResult<CreditNote>.Invalid("text", $"A note must contain 1 to {CreditNoteText.MaxLength} characters.");
        }

        var application = await reader.FindReadableAsync(command.Principal, command.ApplicationId, cancellationToken);
        if (application is null)
        {
            return UseCaseResult<CreditNote>.NotFound();
        }

        var decision = await authorization.AuthorizeAsync(
            command.Principal, new CreditApplicationResource(application), KnownPermissions.CreditNoteWrite, cancellationToken);
        audit.Record(command.Principal, decision);
        if (!decision.IsAllowed)
        {
            return UseCaseResult<CreditNote>.Forbidden(notices.FromDecision(decision)?.Code);
        }

        var note = await repository.AddNoteAsync(command.Principal, application.Id, text!, cancellationToken);
        return note is null ? UseCaseResult<CreditNote>.NotFound() : UseCaseResult<CreditNote>.Success(note);
    }
}
