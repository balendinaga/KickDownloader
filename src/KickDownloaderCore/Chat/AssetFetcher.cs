namespace KickDownloaderCore.Chat;

public interface IAssetFetcher
{
    /// <summary>Downloads an image. Returns null when the asset is missing or unusable (404, 403, too large).</summary>
    Task<byte[]?> GetBytesAsync(Uri uri, CancellationToken ct = default);
}

/// <summary>Fetches emote and badge images; only https hosts under kick.com are allowed.</summary>
public sealed class HttpAssetFetcher(HttpClient http, int maxBytes = 5 * 1024 * 1024) : IAssetFetcher
{
    public static bool IsTrusted(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host == "kick.com" || uri.Host.EndsWith(".kick.com", StringComparison.OrdinalIgnoreCase));

    public async Task<byte[]?> GetBytesAsync(Uri uri, CancellationToken ct = default)
    {
        if (!IsTrusted(uri))
        {
            return null;
        }

        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }
}

/// <summary>Reads pixel size from PNG, GIF and WebP headers without an imaging dependency.</summary>
public static class ImageInfo
{
    public static (int Width, int Height) GetSize(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 24 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
        {
            return (ReadBe(d, 16), ReadBe(d, 20));
        }

        if (d.Length >= 10 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F')
        {
            return (d[6] | (d[7] << 8), d[8] | (d[9] << 8));
        }

        if (d.Length >= 30 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E')
        {
            if (d[12] == 'V' && d[13] == 'P' && d[14] == '8' && d[15] == 'X')
            {
                return (1 + (d[24] | (d[25] << 8) | (d[26] << 16)), 1 + (d[27] | (d[28] << 8) | (d[29] << 16)));
            }

            if (d[12] == 'V' && d[13] == 'P' && d[14] == '8' && d[15] == 'L')
            {
                var bits = (uint)(d[21] | (d[22] << 8) | (d[23] << 16)) | ((uint)d[24] << 24);
                return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
            }

            if (d[12] == 'V' && d[13] == 'P' && d[14] == '8' && d[15] == ' ')
            {
                return ((d[26] | (d[27] << 8)) & 0x3FFF, (d[28] | (d[29] << 8)) & 0x3FFF);
            }
        }

        return (0, 0);
    }

    private static int ReadBe(ReadOnlySpan<byte> d, int i) => (d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3];
}
