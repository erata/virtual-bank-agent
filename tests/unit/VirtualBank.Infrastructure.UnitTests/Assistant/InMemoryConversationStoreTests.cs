using VirtualBank.Infrastructure.Assistant;
using Xunit;

namespace VirtualBank.Infrastructure.UnitTests.Assistant;

public sealed class InMemoryConversationStoreTests
{
    [Fact]
    public async Task Conversation_keeps_user_and_assistant_turns_but_enforces_owner()
    {
        var store = new InMemoryConversationStore();
        var created = await store.LoadAsync("user-1", null, CancellationToken.None);
        Assert.NotNull(created);
        await store.AppendTurnAsync("user-1", created!.Id, "show customer", "authorized answer", CancellationToken.None);
        var reread = await store.LoadAsync("user-1", created.Id, CancellationToken.None);
        Assert.Equal(new[] { "show customer" }, reread!.PriorUserMessages);
        Assert.Equal(new[] { "authorized answer" }, reread.PriorAssistantMessages);
        Assert.Null(await store.LoadAsync("user-2", created.Id, CancellationToken.None));
    }
}
