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

    public int AssetConcurrency { get; init; } = 4;
}

public sealed class KickChatDownloader(KickApiClient api, IAssetFetcher assets)
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Called with the time where paging could not advance exactly, so messages around it may be missing.</summary>
    public Action<DateTimeOffset>? OnPossibleTruncation { get; init; }

    public async Task<ChatRoot> DownloadAsync(
        string vodUuid, string? channelSlug, ChatDownloadOptions? options = null,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        options ??= new ChatDownloadOptions();
        if (options.Start < TimeSpan.Zero || options.Duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Start must be >= 0 and duration > 0.");
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

        var found = await CollectRangeAsync(channel.Id, livestream.StartTime + from, livestream.StartTime + to, progress, ct)
            .ConfigureAwait(false);
        var messages = found;
        var loaded = await LoadAssetsAsync(messages, options.AssetConcurrency, ct).ConfigureAwait(false);
        return ChatConverter.Build(channel, video, messages, loaded, from.TotalSeconds, to.TotalSeconds, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Pages backwards from <paramref name="end"/> with Kick's microsecond cursor (25 messages per request, newest
    /// first) until a page reaches before <paramref name="start"/>. Unlike fixed time windows, a page can never
    /// hide messages in a busy second. Returns the messages in [start, end), oldest first, de-duplicated by id.
    /// </summary>
    public async Task<List<KickChatMessage>> CollectRangeAsync(
        long channelId, DateTimeOffset start, DateTimeOffset end,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var seen = new Dictionary<string, KickChatMessage>();
        var span = end - start;
        var cursor = ToMicros(end);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await api.GetChatBeforeAsync(channelId, cursor, ct).ConfigureAwait(false);
            if (page.Messages.Count == 0)
            {
                break;
            }

            foreach (var m in page.Messages)
            {
                if (m.CreatedAt >= start && m.CreatedAt < end)
                {
                    seen.TryAdd(m.Id, m);
                }
            }

            var oldest = page.Messages.Min(m => m.CreatedAt);
            progress?.Report(Math.Clamp((end - (oldest < start ? start : oldest)) / span, 0, 1));
            if (oldest < start)
            {
                break;
            }

            if (!TryNextCursor(page, oldest, cursor, out var next, out var exact))
            {
                OnPossibleTruncation?.Invoke(oldest);
                break;
            }

            if (!exact)
            {
                OnPossibleTruncation?.Invoke(oldest); // second-level fallback may skip older messages of that second
            }

            cursor = next;
        }

        progress?.Report(1.0);
        return seen.Values.OrderBy(m => m.CreatedAt).ToList();
    }

    /// <summary>The page's own cursor when it moves strictly backwards; else the start of the oldest message's second.</summary>
    private static bool TryNextCursor(KickChatPage page, DateTimeOffset oldest, long current, out long next, out bool exact)
    {
        exact = true;
        if (long.TryParse(page.Cursor, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out next)
            && next < current)
        {
            return true;
        }

        exact = false;
        next = ToMicros(new DateTimeOffset(oldest.Ticks - (oldest.Ticks % TimeSpan.TicksPerSecond), oldest.Offset));
        return next < current;
    }

    private static long ToMicros(DateTimeOffset t) => (t.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;

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
