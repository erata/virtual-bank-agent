using VirtualBank.Application.Assistant;
using VirtualBank.Application.Common;
using Xunit;
using static VirtualBank.Application.UnitTests.Assistant.ScriptedRuntime;

namespace VirtualBank.Application.UnitTests.Assistant;

/// <summary>
/// Language handling and the deterministic "what can I do?" answer.
/// These run with InvariantGlobalization=true (Directory.Build.props), the same as production images,
/// which is exactly where OS-based Unicode normalization used to be a silent no-op.
/// </summary>
public sealed class AssistantLanguageAndCapabilityTests
{
    [Theory]
    [InlineData("çğıİöşüÇĞÖŞÜ", "cgiiosucgosu")]
    [InlineData("Kontostände prüfen", "kontostande prufen")]
    [InlineData("über", "uber")] // already-decomposed input
    public void Normalize_folds_turkish_and_german_letters_without_icu(string input, string expected) =>
        Assert.Equal(expected, NaturalLanguageIntent.Normalize(input));

    [Theory]
    [InlineData("Bu asistanla hangi işlemleri yapabilirim?", AssistantLanguage.Turkish)]
    [InlineData("Bu asistanla neler yapabilirim?", AssistantLanguage.Turkish)]
    [InlineData("Was kann ich mit diesem Assistenten machen?", AssistantLanguage.German)]
    [InlineData("Wie kannst du mir helfen?", AssistantLanguage.German)]
    [InlineData("What can I do with this assistant?", AssistantLanguage.English)]
    [InlineData("Kontostände meiner Kunden", AssistantLanguage.German)]
    [InlineData("müşterilerimin toplam parasını göster", AssistantLanguage.Turkish)]
    public void The_same_question_is_recognised_in_three_languages(string message, AssistantLanguage expected)
    {
        Assert.Equal(expected, NaturalLanguageIntent.Detect(message));
        Assert.True(NaturalLanguageIntent.IsLanguageRecognised(message));
    }

    [Fact]
    public void Unrecognised_text_falls_back_to_english_but_is_reported_as_unrecognised()
    {
        Assert.Equal(AssistantLanguage.English, NaturalLanguageIntent.Detect("ok"));
        Assert.False(NaturalLanguageIntent.IsLanguageRecognised("ok"));
    }

    [Theory]
    [InlineData("Bu asistanla hangi işlemleri yapabilirim?", true)]
    [InlineData("Was kann ich mit diesem Assistenten machen?", true)]
    [InlineData("What can you do?", true)]
    [InlineData("C-1002 için neler yapabilirim?", false)]          // concrete record -> runtime
    [InlineData("Müşterilerim için neler yapabilirim?", false)]     // concrete customer intent -> runtime
    [InlineData("Was kann ich bei der Kreditrichtlinie beachten?", false)] // document question -> runtime
    [InlineData("show my customers", false)]
    public void Only_pure_capability_questions_take_the_deterministic_path(string message, bool expected) =>
        Assert.Equal(expected, AssistantCapabilityAnswer.IsCapabilityQuestion(message));

    [Fact]
    public async Task Capability_question_is_answered_from_permissions_without_calling_the_model()
    {
        var harness = new AssistantHarness();
        var runtime = new ScriptedRuntime("model text", Call("list_customers"));

        var reply = (await harness.AskAsync("anna.berater", runtime, "Bu asistanla hangi işlemleri yapabilirim?")).Value!;

        Assert.False(runtime.Called);
        Assert.Equal(AssistantCapabilityAnswer.Mode, reply.Mode);
        Assert.Empty(reply.ToolCalls);
        Assert.Contains("Erişebildiğiniz müşterileri listeleme", reply.Answer);
        // Advisor has no credit.read and no self-service: those capabilities must not be advertised.
        Assert.DoesNotContain("kredi başvurularını listeleme", reply.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kendi hesaplarınızı", reply.Answer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mehmet.kredit", "Was kann ich mit diesem Assistenten machen?", "Kreditanträge auflisten", "Ihre eigenen Konten")]
    [InlineData("deniz.customer", "What can I do with this assistant?", "Explain your permissions", "List the customers")] // harness registers no self-service or document tools
    [InlineData("lena.admin", "What can you do?", "Explain your permissions", "List the customers")]
    public async Task Capability_answer_is_scoped_to_the_callers_role_and_language(
        string persona, string question, string expected, string forbidden)
    {
        var runtime = new ScriptedRuntime("model text");
        var reply = (await new AssistantHarness().AskAsync(persona, runtime, question)).Value!;

        Assert.False(runtime.Called);
        Assert.Contains(expected, reply.Answer, StringComparison.Ordinal);
        Assert.DoesNotContain(forbidden, reply.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capability_shortcut_never_bypasses_the_assistant_use_policy()
    {
        var runtime = new ScriptedRuntime("model text");
        var result = await new AssistantHarness().AskAsync("tom.auditor", runtime, "What can I do with this assistant?");

        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
        Assert.False(runtime.Called);
    }

    [Theory]
    [InlineData("Bu asistanla müşterilerimi göster", AssistantLanguage.Turkish)]
    [InlineData("Zeig mir meine Kunden", AssistantLanguage.German)]
    [InlineData("show my customers", AssistantLanguage.English)]
    public async Task Runtime_receives_the_detected_reply_language(string question, AssistantLanguage expected)
    {
        var runtime = new ScriptedRuntime("ok");
        await new AssistantHarness().AskAsync("anna.berater", runtime, question);

        Assert.True(runtime.Called);
        Assert.Equal(expected, runtime.LastTurn!.ReplyLanguage);
    }
}
