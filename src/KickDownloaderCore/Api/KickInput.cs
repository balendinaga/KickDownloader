using System.Globalization;
using System.Text.RegularExpressions;

namespace KickDownloaderCore.Api;

/// <summary>Accepts bare ids as well as links pasted from the browser.</summary>
public static partial class KickInput
{
    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex UuidRegex();

    [GeneratedRegex("clip_[A-Za-z0-9]{10,40}")]
    private static partial Regex ClipRegex();

    /// <summary>VOD uuid from "uuid" or "https://kick.com/{channel}/videos/{uuid}".</summary>
    public static bool TryParseVod(string? text, out string uuid) => TryParse(text, UuidRegex(), out uuid);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SlugRegex();

    /// <summary>
    /// VOD id plus, when the input is a link, the channel it names. Links like
    /// "https://kick.com/{channel}/videos/{uuid}" use UUIDv7 ids that only resolve together with the channel.
    /// </summary>
    public static bool TryParseVodRef(string? text, out KickVodRef vod)
    {
        vod = null!;
        if (!TryParseVod(text, out var uuid))
        {
            return false;
        }

        string? slug = null;
        var s = text!.Trim();
        if (s.Contains('/') || s.Contains('.') || s.Contains(':'))
        {
            var candidate = s.Contains("://", StringComparison.Ordinal) ? s : "https://" + s;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                var seg = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (seg.Length >= 3 && seg[1].Equals("videos", StringComparison.OrdinalIgnoreCase) && SlugRegex().IsMatch(seg[0]))
                {
                    slug = seg[0];
                }
            }
        }

        vod = new KickVodRef(uuid, slug);
        return true;
    }

    /// <summary>Clip id from "clip_..." or "https://kick.com/{channel}/clips/clip_..." (also "?clip=clip_...").</summary>
    public static bool TryParseClip(string? text, out string clipId) => TryParse(text, ClipRegex(), out clipId);

    private static bool TryParse(string? text, Regex pattern, out string id)
    {
        id = "";
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        if (s.Contains('/') || s.Contains('.') || s.Contains(':'))
        {
            var candidate = s.Contains("://", StringComparison.Ordinal) ? s : "https://" + s;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp
                || !(uri.Host.Equals("kick.com", StringComparison.OrdinalIgnoreCase)
                     || uri.Host.EndsWith(".kick.com", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            s = uri.PathAndQuery;
        }

        var m = pattern.Match(s);
        if (!m.Success)
        {
            return false;
        }

        // A bare id must be the whole input; inside a link the first match is enough.
        if (!text!.Trim().Contains('/') && !text.Trim().Contains('.') && m.Value.Length != s.Length)
        {
            return false;
        }

        id = m.Value;
        return true;
    }
}

/// <summary>Parses "90", "1:30", "01:30:00" and "1:30:00.5" into a TimeSpan (never negative).</summary>
public static class TimeInput
{
    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = default;
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            if (seconds < 0 || seconds > TimeSpan.FromDays(7).TotalSeconds)
            {
                return false;
            }

            value = TimeSpan.FromSeconds(seconds);
            return true;
        }

        var parts = s.Split(':');
        if (parts.Length is < 2 or > 3
            || !parts.Take(parts.Length - 1).All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            || !double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var last)
            || last is < 0 or >= 60)
        {
            return false;
        }

        var ints = parts.Take(parts.Length - 1).Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        var (h, m) = ints.Length == 2 ? (ints[0], ints[1]) : (0, ints[0]);
        if (m >= 60)
        {
            return false;
        }

        var total = TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(last);
        if (total > TimeSpan.FromDays(7))
        {
            return false;
        }

        value = total;
        return true;
    }

    public static string Format(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
}

/// <summary>Collision-free output paths with safe file names.</summary>
public static class OutputNaming
{
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (cleaned.Length > 80)
        {
            cleaned = cleaned[..80].TrimEnd(' ', '.');
        }

        return cleaned.Length == 0 ? "kick" : cleaned;
    }

    /// <summary>folder/baseName.ext, or "baseName (2).ext" and so on when taken. Never overwrites.</summary>
    public static string UniquePath(string folder, string baseName, string extension)
    {
        var name = Sanitize(baseName);
        var path = Path.Combine(folder, name + extension);
        for (var i = 2; File.Exists(path) && i < 10_000; i++)
        {
            path = Path.Combine(folder, $"{name} ({i}){extension}");
        }

        return path;
    }
}
