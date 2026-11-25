using VirtualBank.Infrastructure.Identity;
using Xunit;

namespace VirtualBank.Infrastructure.UnitTests.Identity;

public sealed class BackchannelRewriteHandlerTests
{
    private static readonly BackchannelRewriteHandler Handler = new(new Uri("http://localhost:8080"), new Uri("http://keycloak:8080"));

    [Fact]
    public void Public_keycloak_urls_are_sent_to_the_internal_host()
    {
        var rewritten = Handler.Rewrite(new Uri("http://localhost:8080/realms/virtual-bank/protocol/openid-connect/certs"));
        Assert.Equal("http://keycloak:8080/realms/virtual-bank/protocol/openid-connect/certs", rewritten!.ToString());
    }

    [Theory]
    [InlineData("http://localhost:9090/realms/virtual-bank")]
    [InlineData("https://localhost:8080/realms/virtual-bank")]
    [InlineData("http://evil.example:8080/realms/virtual-bank")]
    public void Other_urls_are_left_untouched(string url)
    {
        Assert.Equal(url, Handler.Rewrite(new Uri(url))!.ToString());
    }
}
