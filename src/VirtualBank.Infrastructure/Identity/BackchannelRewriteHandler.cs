namespace VirtualBank.Infrastructure.Identity;

/// <summary>
/// Sends backchannel requests for the public Keycloak base URL to the internal container address.
/// Only scheme, host and port of matching requests change; all other requests pass through untouched.
/// </summary>
public sealed class BackchannelRewriteHandler : DelegatingHandler
{
    private readonly Uri _publicBase;
    private readonly Uri _internalBase;

    public BackchannelRewriteHandler(Uri publicBase, Uri internalBase)
        : base(new HttpClientHandler())
    {
        _publicBase = publicBase;
        _internalBase = internalBase;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.RequestUri = Rewrite(request.RequestUri);
        return base.SendAsync(request, cancellationToken);
    }

    public Uri? Rewrite(Uri? uri)
    {
        if (uri is null ||
            !string.Equals(uri.Scheme, _publicBase.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, _publicBase.Host, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != _publicBase.Port)
        {
            return uri;
        }

        return new UriBuilder(uri) { Scheme = _internalBase.Scheme, Host = _internalBase.Host, Port = _internalBase.Port }.Uri;
    }
}
