using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Presentation.Assistant;
using Xunit;

namespace VirtualBank.Presentation.UnitTests;

public sealed class AssistantContractTests
{
    [Fact]
    public void Response_carries_application_computed_status_notices_and_partial_flag()
    {
        var reply = new AssistantReply("answer", "offline",
            [new ToolCallRecord(1, "list_customers", ToolCallStatus.Succeeded, null),
             new ToolCallRecord(2, "list_credit_applications", ToolCallStatus.Denied, "CREDIT_READ_REQUIRED"),
             new ToolCallRecord(3, "get_customer", ToolCallStatus.NotFound, null)],
            ["CREDIT_READ_REQUIRED"]);

        var response = AssistantChatResponse.From(reply);

        Assert.True(response.Partial);
        Assert.Equal(new[] { "SUCCEEDED", "DENIED", "NOT_FOUND" }, response.ToolCalls.Select(c => c.Status));
        Assert.Equal(new[] { "CREDIT_READ_REQUIRED" }, response.Notices);
    }

    [Fact]
    public void Not_found_alone_is_not_partial()
    {
        var reply = new AssistantReply("a", "offline", [new ToolCallRecord(1, "get_customer", ToolCallStatus.NotFound, null)], []);

        Assert.False(AssistantChatResponse.From(reply).Partial);
    }
}
