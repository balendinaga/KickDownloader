using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KickDownloaderCore.Models;

namespace KickDownloaderCore.Api;

public sealed partial class KickApiClient(IKickTransport transport)
{
    private const string Base = "https://kick.com/api";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new KickDateTimeOffsetConverter() },
    };

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^clip_[A-Za-z0-9]{10,40}$")]
    private static partial Regex ClipIdRegex();

    public Task<KickChannel> GetChannelAsync(string slug, CancellationToken ct = default) =>
        GetAsync<KickChannel>(new Uri($"{Base}/v2/channels/{ValidSlug(slug)}"), ct);

    public Task<IReadOnlyList<KickVodSummary>> ListVideosAsync(string slug, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<KickVodSummary>>(new Uri($"{Base}/v2/channels/{ValidSlug(slug)}/videos"), ct);

    /// <summary>Looks a VOD up through whichever API its id family belongs to; both results look the same.</summary>
    public async Task<KickVideo> GetVideoAsync(KickVodRef vod, CancellationToken ct = default)
    {
        if (!Guid.TryParse(vod.Uuid, out var id))
        {
            throw new ArgumentException("VOD id must be a UUID.", nameof(vod));
        }

        if (!vod.IsV7)
        {
            return await GetAsync<KickVideo>(new Uri($"{Base}/v1/video/{id:D}"), ct).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(vod.ChannelSlug))
        {
            throw new KickVodNeedsChannelException(vod.Uuid);
        }

        var channel = await GetChannelAsync(vod.ChannelSlug, ct).ConfigureAwait(false);
        var web = await GetAsync<KickWebEnvelope<KickWebVideo>>(
            new Uri($"https://web.kick.com/api/v1/channels/{channel.Id}/videos/{id:D}"), ct).ConfigureAwait(false);
        return FromWeb(web.Data ?? throw new InvalidDataException($"VOD {id}: response has no 'data' object."), channel);
    }

    private static KickVideo FromWeb(KickWebVideo v, KickChannel channel)
    {
        if (string.IsNullOrEmpty(v.RecordingUrl))
        {
            throw new InvalidDataException($"VOD {v.Id} has no recording (expired, private or still processing).");
        }

        return new KickVideo
        {
            Uuid = v.Id,
            Source = v.RecordingUrl,
            Views = v.ViewerCount,
            Livestream = new KickLivestream
            {
                ChannelId = channel.Id,
                Channel = new KickClipChannel { Id = channel.Id, Slug = v.Channel?.Slug ?? channel.Slug },
                SessionTitle = v.Title,
                StartTime = v.StartTime,
                DurationMs = v.DurationSeconds * 1000,
            },
        };
    }

    public async Task<KickClip> GetClipAsync(string clipId, CancellationToken ct = default)
    {
        if (!ClipIdRegex().IsMatch(clipId))
        {
            throw new ArgumentException("Clip id must look like clip_XXXXXXXXXX.", nameof(clipId));
        }

        var envelope = await GetAsync<KickClipEnvelope>(new Uri($"{Base}/v2/clips/{clipId}"), ct).ConfigureAwait(false);
        return envelope.Clip ?? throw new InvalidDataException($"Clip {clipId}: response has no 'clip' object.");
    }

    public Task<KickClipList> ListClipsAsync(string slug, CancellationToken ct = default) =>
        GetAsync<KickClipList>(new Uri($"{Base}/v2/channels/{ValidSlug(slug)}/clips"), ct);

    /// <summary>
    /// Messages in [start, start + 5s), ascending. Verified: stepping start by 5s leaves no gaps;
    /// the cursor parameter walks backwards and is not used.
    /// </summary>
    public async Task<IReadOnlyList<KickChatMessage>> GetChatWindowAsync(
        long channelId, DateTimeOffset start, CancellationToken ct = default)
    {
        var ts = start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var uri = new Uri($"{Base}/v2/channels/{channelId}/messages?start_time={Uri.EscapeDataString(ts)}");
        var envelope = await GetAsync<KickChatEnvelope>(uri, ct).ConfigureAwait(false);
        return envelope.Data?.Messages ?? [];
    }

    /// <summary>
    /// Messages sent before <paramref name="beforeMicros"/> (Unix time in microseconds), newest first, at most 25 per
    /// page. The returned cursor is the same kind of value (the oldest message's time), so it pages backwards without
    /// gaps or second-level truncation.
    /// </summary>
    public async Task<KickChatPage> GetChatBeforeAsync(long channelId, long beforeMicros, CancellationToken ct = default)
    {
        var uri = new Uri($"{Base}/v2/channels/{channelId}/messages?cursor={beforeMicros.ToString(CultureInfo.InvariantCulture)}");
        var envelope = await GetAsync<KickChatEnvelope>(uri, ct).ConfigureAwait(false);
        return envelope.Data ?? new KickChatPage();
    }

    private async Task<T> GetAsync<T>(Uri uri, CancellationToken ct)
    {
        var body = await transport.GetStringAsync(uri, ct).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json)
                   ?? throw new InvalidDataException($"{uri}: empty JSON body.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{uri}: unexpected response shape. {ex.Message}", ex);
        }
    }

    private static string ValidSlug(string slug) =>
        SlugRegex().IsMatch(slug)
            ? slug
            : throw new ArgumentException("Channel slug may contain only letters, digits, '-' and '_'.", nameof(slug));
}
