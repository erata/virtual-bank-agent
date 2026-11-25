using VirtualBank.Application.Credit.Commands.AddCreditNote;
using VirtualBank.Application.Common;
using VirtualBank.Application.Credit;
using VirtualBank.Application.Credit.Queries.GetCreditApplication;
using VirtualBank.Application.Credit.Queries.ListCreditApplications;
using VirtualBank.Model.Credit;
using VirtualBank.Application.UnitTests.Support;
using Xunit;
using static VirtualBank.Application.UnitTests.Credit.CreditUseCases;

namespace VirtualBank.Application.UnitTests.Credit;

/// <summary>Use cases against a repository that ignores RLS: what the application layer alone guarantees.</summary>
public sealed class CreditUseCaseTests
{
    private static readonly string Mehmet = Personas.SubjectOf("mehmet.kredit");

    private static Task<UseCaseResult<CreditNote>> AddNote(CreditUseCases useCases, string persona, string id, string? text) =>
        useCases.AddNote.HandleAsync(new AddCreditNoteCommand(Personas.Get(persona), id, text), CancellationToken.None);

    [Fact]
    public async Task Advisor_is_forbidden_with_a_deterministic_notice_code_before_any_read()
    {
        var useCases = new CreditUseCases(NewApplication("K-100", CreditApplicationStatus.Submitted, Mehmet));
        var result = await useCases.Get.HandleAsync(new GetCreditApplicationQuery(Personas.Get("anna.berater"), "K-100"), CancellationToken.None);

        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
        Assert.Equal("CREDIT_READ_REQUIRED", result.NoticeCode);
        Assert.Equal(0, useCases.Repository.Reads);
    }

    [Fact]
    public async Task Analyst_reads_assigned_application()
    {
        var useCases = new CreditUseCases(NewApplication("K-100", CreditApplicationStatus.Submitted, Mehmet));
        var result = await useCases.Get.HandleAsync(new GetCreditApplicationQuery(Personas.Get("mehmet.kredit"), "K-100"), CancellationToken.None);

        Assert.Equal(CreditApplicationStatus.Submitted, result.Value!.Status);
    }

    [Fact]
    public async Task Unassigned_application_returned_by_a_broken_store_is_hidden_and_audited()
    {
        var useCases = new CreditUseCases(NewApplication("K-200", CreditApplicationStatus.Draft, analyst: null));
        var result = await useCases.Get.HandleAsync(new GetCreditApplicationQuery(Personas.Get("mehmet.kredit"), "K-200"), CancellationToken.None);

        Assert.Equal(UseCaseStatus.NotFound, result.Status);
        Assert.Single(useCases.Audit.Mismatches);
    }

    [Fact]
    public async Task Malformed_id_is_not_found_without_a_read()
    {
        var useCases = new CreditUseCases();
        var result = await useCases.Get.HandleAsync(new GetCreditApplicationQuery(Personas.Get("mehmet.kredit"), "K-100' OR 1=1"), CancellationToken.None);

        Assert.Equal(UseCaseStatus.NotFound, result.Status);
        Assert.Equal(0, useCases.Repository.Reads);
    }

    [Fact]
    public async Task Approver_list_excludes_drafts_even_if_returned()
    {
        var useCases = new CreditUseCases(
            NewApplication("K-100", CreditApplicationStatus.Submitted, Mehmet),
            NewApplication("K-200", CreditApplicationStatus.Draft, analyst: null));

        var result = await useCases.List.HandleAsync(new ListCreditApplicationsQuery(Personas.Get("max.approver")), CancellationToken.None);

        Assert.Equal(new[] { "K-100" }, result.Value!.Select(a => a.Id));
    }

    [Fact]
    public async Task Analyst_adds_trimmed_note_to_assigned_application()
    {
        var useCases = new CreditUseCases(NewApplication("K-100", CreditApplicationStatus.Submitted, Mehmet));
        var result = await AddNote(useCases, "mehmet.kredit", "K-100", "  Income verified.  ");

        Assert.Equal(UseCaseStatus.Succeeded, result.Status);
        Assert.Equal(new[] { "Income verified." }, useCases.Repository.InsertedNotes);
    }

    [Fact]
    public async Task Approver_without_note_permission_is_forbidden_and_nothing_is_written()
    {
        var useCases = new CreditUseCases(NewApplication("K-100", CreditApplicationStatus.Submitted, Mehmet));
        var result = await AddNote(useCases, "max.approver", "K-100", "Looks fine.");

        Assert.Equal("CREDIT_NOTE_PERMISSION_REQUIRED", result.NoticeCode);
        Assert.Empty(useCases.Repository.InsertedNotes);
    }

    [Fact]
    public async Task Note_on_application_not_visible_is_not_found()
    {
        var useCases = new CreditUseCases(NewApplication("K-200", CreditApplicationStatus.Draft, analyst: null));
        var result = await AddNote(useCases, "mehmet.kredit", "K-200", "Note");

        Assert.Equal(UseCaseStatus.NotFound, result.Status);
        Assert.Empty(useCases.Repository.InsertedNotes);
    }

    [Fact]
    public async Task Note_rejected_by_the_database_is_not_found()
    {
        var useCases = new CreditUseCases { Repository = new UnfilteredRepository(NewApplication("K-100", CreditApplicationStatus.Submitted, Mehmet)) { RejectInsert = true } };
        var result = await AddNote(useCases, "mehmet.kredit", "K-100", "Note");

        Assert.Equal(UseCaseStatus.NotFound, result.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_note_is_invalid_without_a_read(string text)
    {
        var useCases = new CreditUseCases(NewApplication("K-100", CreditApplicationStatus.Submitted, Mehmet));
        var result = await AddNote(useCases, "mehmet.kredit", "K-100", text);

        Assert.Equal(UseCaseStatus.Invalid, result.Status);
        Assert.Equal(0, useCases.Repository.Reads);
    }

    [Fact]
    public async Task Oversized_note_is_invalid()
    {
        var useCases = new CreditUseCases(NewApplication("K-100", CreditApplicationStatus.Submitted, Mehmet));
        var result = await AddNote(useCases, "mehmet.kredit", "K-100", new string('x', CreditNoteText.MaxLength + 1));

        Assert.Equal(UseCaseStatus.Invalid, result.Status);
    }
}
