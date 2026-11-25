using System.Net;
using VirtualBank.Infrastructure.Identity;
using Xunit;

namespace VirtualBank.Infrastructure.UnitTests.Identity;

public sealed class ReadinessTests
{
    private static readonly KeycloakOptions Keycloak = new() { Issuer = "http://kc.test/realms/virtual-bank" };

    /// <summary>Serves canned responses by absolute URL; anything else is a 404.</summary>
    private sealed class StubHandler(Dictionary<string, (HttpStatusCode Status, string Body)> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses.TryGetValue(request.RequestUri!.ToString(), out var r)
                ? new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private const string Discovery = "http://kc.test/realms/virtual-bank/.well-known/openid-configuration";
    private const string Jwks = "http://kc.test/realms/virtual-bank/protocol/openid-connect/certs";

    private static Task<bool> Probe(Dictionary<string, (HttpStatusCode, string)> responses) =>
        SigningKeyProbe.AvailableAsync(new HttpClient(new StubHandler(responses)), Keycloak, CancellationToken.None);

    private const string GoodDiscovery = $$"""{"issuer":"http://kc.test/realms/virtual-bank","jwks_uri":"{{Jwks}}"}""";

    private const string SigningKey = """{"kid":"k1","kty":"RSA","alg":"RS256","use":"sig","n":"sXch","e":"AQAB"}""";

    private static Task<bool> ProbeWith(string discovery, string jwks) =>
        Probe(new() { [Discovery] = (HttpStatusCode.OK, discovery), [Jwks] = (HttpStatusCode.OK, jwks) });

    [Fact]
    public async Task Ready_when_issuer_matches_and_an_rs256_signing_key_exists()
    {
        Assert.True(await ProbeWith(GoodDiscovery, $$"""{"keys":[{{SigningKey}}]}"""));
    }

    [Fact]
    public async Task Ready_when_signing_key_sits_next_to_an_encryption_key()
    {
        // Keycloak publishes an RSA-OAEP encryption key alongside the RS256 signing key.
        Assert.True(await ProbeWith(GoodDiscovery,
            $$"""{"keys":[{"kid":"e1","kty":"RSA","alg":"RSA-OAEP","use":"enc","n":"x","e":"AQAB"},{{SigningKey}}]}"""));
    }

    [Fact]
    public async Task Not_ready_when_jwks_is_empty()
    {
        Assert.False(await ProbeWith(GoodDiscovery, """{"keys":[]}"""));
    }

    [Theory]
    [InlineData("""{"kid":"e1","kty":"RSA","alg":"RSA-OAEP","use":"enc","n":"x","e":"AQAB"}""")]
    [InlineData("""{"kid":"h1","kty":"oct","alg":"HS256","k":"c2VjcmV0"}""")]
    [InlineData("""{"kid":"ec","kty":"EC","alg":"ES256","use":"sig","crv":"P-256","x":"a","y":"b"}""")]
    [InlineData("""{"kid":"k1","kty":"RSA","alg":"RS256","use":"sig"}""")]
    public async Task Not_ready_without_a_usable_rs256_signing_key(string key)
    {
        Assert.False(await ProbeWith(GoodDiscovery, $$"""{"keys":[{{key}}]}"""));
    }

    [Fact]
    public async Task Not_ready_when_discovery_names_another_issuer()
    {
        Assert.False(await ProbeWith($$"""{"issuer":"http://kc.test/realms/other","jwks_uri":"{{Jwks}}"}""", $$"""{"keys":[{{SigningKey}}]}"""));
    }

    [Fact]
    public async Task Not_ready_when_jwks_uri_points_outside_the_issuer()
    {
        const string foreignJwks = "http://attacker.test/certs";
        Assert.False(await Probe(new()
        {
            [Discovery] = (HttpStatusCode.OK, $$"""{"issuer":"http://kc.test/realms/virtual-bank","jwks_uri":"{{foreignJwks}}"}"""),
            [foreignJwks] = (HttpStatusCode.OK, $$"""{"keys":[{{SigningKey}}]}"""),
        }));
    }

    [Fact]
    public async Task Ready_when_keycloak_advertises_jwks_on_the_internal_backchannel_host()
    {
        var keycloak = new KeycloakOptions
        {
            Issuer = "http://localhost:8080/realms/virtual-bank", PublicBaseUrl = "http://localhost:8080", InternalBaseUrl = "http://keycloak:8080",
        };
        const string discovery = "http://localhost:8080/realms/virtual-bank/.well-known/openid-configuration";
        const string internalJwks = "http://keycloak:8080/realms/virtual-bank/protocol/openid-connect/certs";
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            [discovery] = (HttpStatusCode.OK, $$"""{"issuer":"http://localhost:8080/realms/virtual-bank","jwks_uri":"{{internalJwks}}"}"""),
            [internalJwks] = (HttpStatusCode.OK, $$"""{"keys":[{{SigningKey}}]}"""),
        };

        Assert.True(await SigningKeyProbe.AvailableAsync(new HttpClient(new StubHandler(responses)), keycloak, CancellationToken.None));
    }

    [Fact]
    public async Task Not_ready_when_jwks_endpoint_fails()
    {
        Assert.False(await Probe(new() { [Discovery] = (HttpStatusCode.OK, GoodDiscovery) }));
    }

    [Fact]
    public async Task Not_ready_when_discovery_has_no_jwks_uri_or_is_not_json()
    {
        Assert.False(await Probe(new() { [Discovery] = (HttpStatusCode.OK, """{"issuer":"http://kc.test/realms/virtual-bank"}""") }));
        Assert.False(await Probe(new() { [Discovery] = (HttpStatusCode.OK, "<html>maintenance</html>") }));
    }

    [Fact]
    public async Task Not_ready_when_discovery_is_unreachable()
    {
        Assert.False(await Probe(new()));
    }
}
