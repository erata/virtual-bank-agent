using VirtualBank.Application.Credit;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Credit.Abstractions;

namespace VirtualBank.Application.Credit.Queries.ListCreditNotes;

/// <summary>A note as shown to readers of the application. <see cref="ByYou"/> instead of exposing other users' IDs.</summary>
public sealed record CreditNoteView(long Id, bool ByYou, string Body, DateTimeOffset CreatedAt);

public sealed record ListCreditNotesQuery(PrincipalContext Principal, string ApplicationId);

/// <summary>
/// Notes of one application, including decision notes ("[REJECTED] reason"). Visible exactly when the application
/// is visible: same read path, application policy and RLS (notes_read depends on the application being readable).
/// </summary>
public sealed class ListCreditNotesHandler(CreditApplicationReader reader, ICreditApplicationRepository repository, NoticeCatalog notices)
{
    public async Task<UseCaseResult<IReadOnlyList<CreditNoteView>>> HandleAsync(ListCreditNotesQuery query, CancellationToken ct)
    {
        if (!CreditApplicationReader.HasAnyReadPath(query.Principal))
            return UseCaseResult<IReadOnlyList<CreditNoteView>>.Forbidden(
                notices.HintCodeFor(KnownPermissions.CreditRead, ReasonCodes.MissingPermission));
        var application = await reader.FindReadableAsync(query.Principal, query.ApplicationId, ct);
        if (application is null) return UseCaseResult<IReadOnlyList<CreditNoteView>>.NotFound();
        var notes = await repository.ListNotesAsync(query.Principal, application.Id, ct);
        // A bank customer reading their own request sees the decision (and its reason), not internal staff notes.
        var staff = query.Principal.HasPermission(KnownPermissions.CreditRead) || query.Principal.HasPermission(KnownPermissions.CreditApprove);
        if (!staff) notes = notes.Where(n => n.Body.StartsWith("[APPROVED]", StringComparison.Ordinal) ||
                                             n.Body.StartsWith("[REJECTED]", StringComparison.Ordinal)).ToArray();
        return UseCaseResult<IReadOnlyList<CreditNoteView>>.Success(notes
            .Select(n => new CreditNoteView(n.Id, n.AuthorId == query.Principal.SubjectId, n.Body, n.CreatedAt))
            .ToArray());
    }
}
