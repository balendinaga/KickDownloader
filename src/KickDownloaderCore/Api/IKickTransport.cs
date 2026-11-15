namespace KickDownloaderCore.Api;

/// <summary>HTTP seam. A TLS-impersonating fallback can implement this if Cloudflare starts returning 403.</summary>
public interface IKickTransport
{
    Task<string> GetStringAsync(Uri uri, CancellationToken ct = default);
}

/// <summary>Thrown when Kick/Cloudflare answers 403, the trigger for a TLS-impersonation fallback.</summary>
public sealed class KickBlockedException(Uri uri)
    : Exception($"Kick returned 403 for {uri}. Cloudflare TLS/bot protection likely blocked the request.")
{
    public Uri Uri { get; } = uri;
}
