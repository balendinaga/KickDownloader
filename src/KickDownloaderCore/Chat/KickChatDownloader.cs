using System.Text.Encodings.Web;
using System.Text.Json;
using KickDownloaderCore.Api;
using TwitchDownloaderCore.TwitchObjects;
using KickDownloaderCore.Models;

namespace KickDownloaderCore.Chat;

public sealed record ChatDownloadOptions
{
    public TimeSpan Start { get; init; } = TimeSpan.Zero;

    /// <summary>Null = until the end of the VOD.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>A window returning at least this many messages may be truncated, so it is re-queried (see CollectWindowAsync).</summary>
    public int SplitThreshold { get; init; } = 50;

    public int AssetConcurrency { get; init; } = 4;
}

public sealed class KickChatDownloader(KickApiClient api, IAssetFetcher assets)
{
    public static readonly TimeSpan WindowLength = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Called with a window start when even re-querying could not prove the window complete.</summary>
    public Action<DateTimeOffset>? OnPossibleTruncation { get; init; }

    public async Task<ChatRoot> DownloadAsync(
        string vodUuid, string? channelSlug, ChatDownloadOptions? options = null,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        options ??= new ChatDownloadOptions();
        if (options.Start < TimeSpan.Zero || options.Duration <= TimeSpan.Zero || options.SplitThreshold < 2)
        {
            throw new ArgumentException("Start must be >= 0, duration > 0, split threshold >= 2.");
        }

        var video = await api.GetVideoAsync(new KickVodRef(vodUuid, channelSlug), ct).ConfigureAwait(false);
        var livestream = video.Livestream ?? throw new InvalidDataException("VOD response has no livestream.");
        var slug = string.IsNullOrWhiteSpace(channelSlug) ? livestream.Channel?.Slug : channelSlug;
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Kick did not report the channel of this VOD; pass the channel name.");
        }

        var channel = await api.GetChannelAsync(slug, ct).ConfigureAwait(false);
        if (livestream.ChannelId != channel.Id)
        {
            throw new ArgumentException($"VOD {vodUuid} belongs to channel {livestream.ChannelId}, not '{slug}'.");
        }

        var total = TimeSpan.FromMilliseconds(livestream.DurationMs);
        var from = options.Start;
        var to = options.Duration is { } d ? from + d : total;
        if (to > total)
        {
            to = total;
        }

        if (from >= to)
        {
            throw new ArgumentException($"Start {from} is beyond the VOD length {total}.");
        }

        var byId = new Dictionary<string, KickChatMessage>();
        var order = new List<string>();
        var span = to - from;
        for (var t = from; t < to; t += WindowLength)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var m in await CollectWindowAsync(channel.Id, livestream.StartTime + t, options.SplitThreshold, ct)
                         .ConfigureAwait(false))
            {
                var offset = m.CreatedAt - livestream.StartTime;
                if (offset >= from && offset < to && byId.TryAdd(m.Id, m))
                {
                    order.Add(m.Id);
                }
            }

            progress?.Report(Math.Min(1.0, (t - from + WindowLength) / span));
        }

        var messages = order.Select(id => byId[id]).OrderBy(m => m.CreatedAt).ToList();
        var loaded = await LoadAssetsAsync(messages, options.AssetConcurrency, ct).ConfigureAwait(false);
        return ChatConverter.Build(channel, video, messages, loaded, from.TotalSeconds, to.TotalSeconds, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// One 5s window. start_time always spans 5s even when shifted by 1s, so a full page is not split into
    /// fixed sub-windows: the window is re-queried from the last received second until a page comes back
    /// below the threshold. Messages are de-duplicated by id.
    /// </summary>
    public async Task<IReadOnlyList<KickChatMessage>> CollectWindowAsync(
        long channelId, DateTimeOffset start, int splitThreshold, CancellationToken ct = default)
    {
        var end = start + WindowLength;
        var seen = new Dictionary<string, KickChatMessage>();
        var cursor = start;
        while (true)
        {
            var page = await api.GetChatWindowAsync(channelId, cursor, ct).ConfigureAwait(false);
            foreach (var m in page)
            {
                seen.TryAdd(m.Id, m);
            }

            if (page.Count < splitThreshold)
            {
                break;
            }

            var lastSecond = page.Max(m => m.CreatedAt);
            lastSecond = new DateTimeOffset(lastSecond.Ticks - (lastSecond.Ticks % TimeSpan.TicksPerSecond), lastSecond.Offset);
            var stuck = lastSecond <= cursor;
            if (stuck)
            {
                // A full page that fits in one second: moving on may skip messages of that second.
                OnPossibleTruncation?.Invoke(cursor);
            }

            var next = stuck ? cursor + TimeSpan.FromSeconds(1) : lastSecond;
            if (next >= end)
            {
                break;
            }

            cursor = next;
        }

        return seen.Values.OrderBy(m => m.CreatedAt).ToList();
    }

    private async Task<ChatAssets> LoadAssetsAsync(
        IReadOnlyList<KickChatMessage> messages, int concurrency, CancellationToken ct)
    {
        var emoteNames = messages.SelectMany(m => ChatConverter.EmotesIn(m.Content))
            .GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First().Name);
        var badgeUrls = messages.SelectMany(ChatConverter.BadgesOf)
            .Where(b => b.ImageUrl is not null)
            .GroupBy(b => (b.Name, b.Version)).ToDictionary(g => g.Key, g => g.First().ImageUrl!);

        using var gate = new SemaphoreSlim(Math.Max(1, concurrency));

        async Task<T?> Limited<T>(Func<Task<T?>> work) where T : class
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await work().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                return null; // one broken image must not fail the whole chat download
            }
            finally
            {
                gate.Release();
            }
        }

        var emoteTasks = emoteNames.Select(e => Limited(async () =>
        {
            var bytes = await assets.GetBytesAsync(new Uri($"https://files.kick.com/emotes/{e.Key}/fullsize"), ct).ConfigureAwait(false);
            if (bytes is null)
            {
                return null;
            }

            var (w, h) = ImageInfo.GetSize(bytes);
            return (EmoteAsset?)new EmoteAsset(e.Value, bytes, w, h);
        }).ContinueWith(t => (e.Key, Asset: t.Result), TaskScheduler.Default)).ToList();

        var badgeTasks = badgeUrls.Select(b => Limited(async () =>
        {
            if (!Uri.TryCreate(b.Value, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var bytes = await assets.GetBytesAsync(uri, ct).ConfigureAwait(false);
            return bytes is null ? null : new BadgeAsset(bytes, b.Value);
        }).ContinueWith(t => (b.Key, Asset: t.Result), TaskScheduler.Default)).ToList();

        var emotes = (await Task.WhenAll(emoteTasks).ConfigureAwait(false))
            .Where(r => r.Asset is not null).ToDictionary(r => r.Key, r => r.Asset!);
        var badges = (await Task.WhenAll(badgeTasks).ConfigureAwait(false))
            .Where(r => r.Asset is not null).ToDictionary(r => r.Key, r => r.Asset!);
        return new ChatAssets(emotes, badges);
    }

    public static async Task WriteAsync(ChatRoot root, string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
        {
            throw new IOException($"Output file already exists: {path}");
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            await JsonSerializer.SerializeAsync(stream, root, WriteOptions, ct).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                File.Delete(path); // half-written file of a failed or cancelled write
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw;
        }
    }
}
