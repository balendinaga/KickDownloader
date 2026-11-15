using System.Text.RegularExpressions;

namespace KickDownloaderCore.Video;

public sealed record HlsVariant(string Name, int Bandwidth, string? Resolution, Uri Url);

public static partial class HlsMaster
{
    [GeneratedRegex("""([A-Z0-9-]+)=("[^"]*"|[^,]*)""")]
    private static partial Regex AttrRegex();

    /// <summary>Parses #EXT-X-STREAM-INF entries of a master playlist; relative URIs resolve against <paramref name="baseUri"/>.</summary>
    public static IReadOnlyList<HlsVariant> Parse(string playlist, Uri baseUri)
    {
        var lines = playlist.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var variants = new List<HlsVariant>();
        for (var i = 0; i < lines.Length - 1; i++)
        {
            const string tag = "#EXT-X-STREAM-INF:";
            if (!lines[i].StartsWith(tag, StringComparison.Ordinal))
            {
                continue;
            }

            var attrs = AttrRegex().Matches(lines[i][tag.Length..])
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim('"'));
            var uri = lines[i + 1];
            if (uri.StartsWith('#'))
            {
                continue;
            }

            attrs.TryGetValue("BANDWIDTH", out var bw);
            attrs.TryGetValue("RESOLUTION", out var res);
            attrs.TryGetValue("VIDEO", out var name);
            variants.Add(new HlsVariant(
                name ?? res ?? $"variant{variants.Count}",
                int.TryParse(bw, out var b) ? b : 0,
                res,
                new Uri(baseUri, uri)));
        }

        return variants;
    }

    /// <summary>Exact name match (case-insensitive, e.g. "720p60" or "720p"), else highest bandwidth when null.</summary>
    public static HlsVariant Select(IReadOnlyList<HlsVariant> variants, string? quality)
    {
        if (variants.Count == 0)
        {
            throw new InvalidDataException("Master playlist contains no stream variants.");
        }

        if (string.IsNullOrWhiteSpace(quality))
        {
            return variants.MaxBy(v => v.Bandwidth)!;
        }

        return variants.FirstOrDefault(v => v.Name.Equals(quality, StringComparison.OrdinalIgnoreCase))
               ?? variants.Where(v => v.Name.StartsWith(quality, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(v => v.Bandwidth).FirstOrDefault()
               ?? throw new ArgumentException(
                   $"Quality '{quality}' not found. Available: {string.Join(", ", variants.Select(v => v.Name))}");
    }
}
