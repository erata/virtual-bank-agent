using System.Data.Common;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Credit.Commands.ApproveCreditApplication;
using VirtualBank.Model.Credit;

namespace VirtualBank.Infrastructure.Persistence.Credit;

/// <summary>Connection for the credit_approval role: the only database identity that may UPDATE an application.</summary>
public sealed record CreditApprovalDatabase(DbDataSource DataSource);

/// <summary>
/// Executes a confirmed credit decision in ONE transaction under the caller's RLS context:
/// idempotency key → row lock → version → domain rules → UPDATE (RLS USING + WITH CHECK) → decision note →
/// decision log. Any refusal leaves the application unchanged.
/// </summary>
public sealed class PostgresCreditApprovalExecutor(CreditApprovalDatabase database) : ICreditApprovalExecutor
{
    private const string InsufficientPrivilege = "42501";

    public async Task<ApprovalExecution> DecideAsync(PrincipalContext principal, CreditApplicationId applicationId,
        int expectedVersion, Guid idempotencyKey, CreditDecision decision, string? note, DateTimeOffset decidedAt,
        CancellationToken ct)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, ct);

        await using (var seen = session.CreateCommand(
            "SELECT outcome FROM credit.approval_requests WHERE idempotency_key = @key").AddParameter("key", idempotencyKey))
        {
            if (await seen.ExecuteScalarAsync(ct) is string)
                return new ApprovalExecution(ApprovalExecutionOutcome.AlreadyExecuted, expectedVersion + 1);
        }

        CreditApplication? application;
        await using (var find = session.CreateCommand(PostgresCreditApplicationRepository.FindApplicationSql + " FOR UPDATE")
                         .AddParameter("application_id", applicationId.Value))
        {
            application = (await PostgresCreditApplicationRepository.ReadAsync(find, ct)).SingleOrDefault();
        }
        if (application is null) return new ApprovalExecution(ApprovalExecutionOutcome.Refused, expectedVersion);
        if (application.Version != expectedVersion)
            return new ApprovalExecution(ApprovalExecutionOutcome.VersionChanged, application.Version);

        var limit = principal.ApprovalLimit is { } value ? Money.Eur(value) : (Money?)null;
        if (application.CheckApproval(principal.SubjectId, limit) != ApprovalCheck.Allowed)
            return new ApprovalExecution(ApprovalExecutionOutcome.Refused, application.Version);
        if (decision == CreditDecision.Approve) application.Approve(principal.SubjectId, limit, decidedAt);
        else application.Reject(principal.SubjectId, limit);

        try
        {
            var outcome = decision == CreditDecision.Approve ? "APPROVED" : "REJECTED";
            // Note first: the notes policy requires the application to be visible, which a REJECTED one no longer
            // is for the decider. Everything below is one transaction, so a refused UPDATE also drops the note.
            if (note is not null)
            {
                await using var insertNote = session.CreateCommand("""
                        INSERT INTO credit.credit_notes (application_id, author_id, body) VALUES (@id, @by, @body)
                        """)
                    .AddParameter("id", applicationId.Value).AddParameter("by", principal.SubjectId)
                    .AddParameter("body", $"[{outcome}] {note}");
                await insertNote.ExecuteNonQueryAsync(ct);
            }

            // Decision record BEFORE the UPDATE: PostgreSQL checks the new row against the SELECT policies too, and
            // the decider may see a REJECTED row only through this record (applications_read_decided).
            await using (var record = session.CreateCommand("""
                    INSERT INTO credit.approval_requests (idempotency_key, application_id, requested_by, outcome)
                    VALUES (@key, @id, @by, @outcome)
                    """)
                .AddParameter("key", idempotencyKey).AddParameter("id", applicationId.Value)
                .AddParameter("by", principal.SubjectId).AddParameter("outcome", outcome))
            {
                await record.ExecuteNonQueryAsync(ct);
            }

            var sql = decision == CreditDecision.Approve
                ? """
                  UPDATE credit.credit_applications
                  SET status = 'APPROVED', approved_by = @decider, approved_at = @at, version = @new_version
                  WHERE id = @id AND version = @expected
                  """
                : """
                  UPDATE credit.credit_applications
                  SET status = 'REJECTED', version = @new_version
                  WHERE id = @id AND version = @expected
                  """;
            await using (var update = session.CreateCommand(sql)
                .AddParameter("decider", principal.SubjectId).AddParameter("at", decidedAt)
                .AddParameter("new_version", application.Version).AddParameter("id", applicationId.Value)
                .AddParameter("expected", expectedVersion))
            {
                // 0 rows: RLS USING filtered it (state/limit/four-eyes) or the version moved. Not committed:
                // the note and the decision record above are rolled back with the transaction.
                if (await update.ExecuteNonQueryAsync(ct) != 1)
                    return new ApprovalExecution(ApprovalExecutionOutcome.Refused, expectedVersion);
            }

            await session.CommitAsync(ct);
            return new ApprovalExecution(
                decision == CreditDecision.Approve ? ApprovalExecutionOutcome.Approved : ApprovalExecutionOutcome.Rejected,
                application.Version);
        }
        catch (DbException ex) when (ex.SqlState == InsufficientPrivilege)
        {
            // RLS WITH CHECK rejected the new row state; the transaction is rolled back on dispose.
            return new ApprovalExecution(ApprovalExecutionOutcome.Refused, expectedVersion);
        }
    }
}

/// <summary>Used when no approval connection is configured: fails closed, never decides.</summary>
public sealed class DisabledCreditApprovalExecutor : ICreditApprovalExecutor
{
    public Task<ApprovalExecution> DecideAsync(PrincipalContext principal, CreditApplicationId applicationId, int expectedVersion,
        Guid idempotencyKey, CreditDecision decision, string? note, DateTimeOffset decidedAt, CancellationToken cancellationToken) =>
        Task.FromResult(new ApprovalExecution(ApprovalExecutionOutcome.Refused, expectedVersion));
}
