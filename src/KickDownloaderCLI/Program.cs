using System.Globalization;
using System.Text.RegularExpressions;
using KickDownloaderCore.Api;
using KickDownloaderCore.Chat;
using KickDownloaderCore.Rendering;
using KickDownloaderCore.Video;

const string Usage = """
    KickDownloader
      check [slug]                                  live read-only API check (default slug: xqc)
      clip <clip id|url> <out.mp4>                  download a clip
      vod <uuid> <out.mp4> [--start T] [--duration T] [--quality 720p60]
      chat <slug> <vod uuid> <out.json> [--start T] [--duration T]
      render <chat.json> <out.mov|.webm|.mp4> [--format mov|webm|mp4] [--width 350] [--height 600] [--fps 30]
             [--background #00000000] [--message-color #ffffff] [--font-size 12] [--update-rate 0.2]
             [--start T] [--end T] [--timestamp true] [--outline true] [--emote-scale 1.0] [--verbose true]
    T = seconds or hh:mm:ss. Optional: --ffmpeg <path>
    """;

if (args.Length == 0)
{
    Console.WriteLine(Usage);
    return 1;
}

var transport = HttpClientTransport.CreateDefault();
transport.RateLimited += n =>
{
    if (n.SecondsLeft > 0 && n.SecondsLeft % 5 == 0)
        Console.Error.WriteLine($"Rate limited, waiting {n.SecondsLeft}s then resuming (retry {n.Attempt}/{n.MaxAttempts})...");
};
var api = new KickApiClient(transport);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    var opts = ParseOptions(args.Skip(1).ToArray(), out var positional);
    var ffmpeg = new FfmpegRunner(opts.GetValueOrDefault("ffmpeg", "ffmpeg"));
    switch (args[0])
    {
        case "check":
            return await Check(api, positional.FirstOrDefault() ?? "xqc", cts.Token);
        case "clip" when positional.Count == 2:
            var m = Regex.Match(positional[0], "clip_[A-Za-z0-9]+");
            if (!m.Success) throw new ArgumentException("Could not find a clip_... id in the argument.");
            var clipInfo = await api.GetClipAsync(m.Value, cts.Token);
            Console.WriteLine(await new ClipDownloader(api, ffmpeg).DownloadAsync(m.Value, positional[1], cts.Token));
            if (clipInfo.Vod is not null && clipInfo.Channel is not null && clipInfo.VodStartsAtSeconds is { } vs)
            {
                Console.Error.WriteLine(
                    $"matching chat: chat {clipInfo.Channel.Slug} {clipInfo.Vod.Id} <chat.json> --start {vs} --duration {clipInfo.DurationSeconds}");
            }

            return 0;
        case "vod" when positional.Count == 2:
            if (!KickInput.TryParseVodRef(positional[0], out var vodRef))
                throw new ArgumentException("Could not find a VOD id (UUID) in the first argument.");
            Console.WriteLine(await new VodDownloader(api, transport, ffmpeg).DownloadAsync(
                vodRef, positional[1],
                opts.TryGetValue("start", out var s) ? ParseTime(s) : null,
                opts.TryGetValue("duration", out var d) ? ParseTime(d) : null,
                opts.GetValueOrDefault("quality"), cts.Token));
            return 0;
        case "chat" when positional.Count == 3:
            using (var assetHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                assetHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                var downloader = new KickChatDownloader(api, new HttpAssetFetcher(assetHttp))
                {
                    OnPossibleTruncation = at => Console.Error.WriteLine($"warning: chat paging could not advance exactly near {at:u}; some messages may be missing"),
                };
                var progress = new Progress<double>(p => Console.Error.Write($"\rchat {p:P0}   "));
                if (!KickInput.TryParseVodRef(positional[1], out var chatVod))
                    throw new ArgumentException("Could not find a VOD id (UUID) in the second argument.");
                var root = await downloader.DownloadAsync(chatVod.Uuid, positional[0],
                    new ChatDownloadOptions
                    {
                        Start = opts.TryGetValue("start", out var cs) ? ParseTime(cs) : TimeSpan.Zero,
                        Duration = opts.TryGetValue("duration", out var cd) ? ParseTime(cd) : null,
                    }, progress, cts.Token);
                await KickChatDownloader.WriteAsync(root, positional[2], cts.Token);
                Console.Error.WriteLine();
                Console.WriteLine($"{positional[2]}: {root.comments.Count} comments, {root.embeddedData.firstParty.Count} emotes, {root.embeddedData.twitchBadges.Count} badge types");
            }

            return 0;
        case "render" when positional.Count == 2:
            var settings = new RenderSettings
            {
                InputFile = positional[0],
                OutputFile = positional[1],
                Width = IntOpt(opts, "width", 350),
                Height = IntOpt(opts, "height", 600),
                Fps = IntOpt(opts, "fps", 30),
                Format = opts.GetValueOrDefault("format", "mov").ToLowerInvariant() switch
                {
                    "mov" => RenderFormat.MovProRes,
                    "webm" => RenderFormat.WebmVp9,
                    "mp4" => RenderFormat.Mp4,
                    var other => throw new ArgumentException($"Unknown format '{other}'. Use mov, webm or mp4."),
                },
                BackgroundColor = opts.GetValueOrDefault("background", "#00000000"),
                MessageColor = opts.GetValueOrDefault("message-color", "#ffffff"),
                FontSize = DoubleOpt(opts, "font-size", 12),
                UpdateRate = DoubleOpt(opts, "update-rate", 0.2),
                EmoteScale = DoubleOpt(opts, "emote-scale", 1.0),
                Timestamp = opts.GetValueOrDefault("timestamp") == "true",
                Outline = opts.GetValueOrDefault("outline") == "true",
                Start = opts.TryGetValue("start", out var rs) ? ParseTime(rs) : null,
                End = opts.TryGetValue("end", out var re) ? ParseTime(re) : null,
                FfmpegPath = opts.GetValueOrDefault("ffmpeg", "ffmpeg"),
            };
            await ChatRenderService.RenderAsync(settings, new ConsoleTaskProgress(opts.GetValueOrDefault("verbose") == "true"), cts.Token);
            Console.Error.WriteLine();
            Console.WriteLine(positional[1]);
            return 0;
        default:
            Console.WriteLine(Usage);
            return 1;
    }
}
catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or InvalidOperationException
                               or HttpRequestException or KickBlockedException or OperationCanceledException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}

static async Task<int> Check(KickApiClient api, string slug, CancellationToken ct)
{
    var ch = await api.GetChannelAsync(slug, ct);
    Console.WriteLine($"channel   {ch.Slug} id={ch.Id} vod_enabled={ch.VodEnabled}");
    var vods = await api.ListVideosAsync(slug, ct);
    Console.WriteLine($"videos    {vods.Count} listed");
    var vod = vods.First(v => v.Video is not null);
    var video = await api.GetVideoAsync(vod.Video!.Uuid, ct);
    Console.WriteLine($"video     {video.Uuid} start={video.Livestream?.StartTime:u} dur={TimeSpan.FromMilliseconds(vod.DurationMs)} source={video.Source}");
    var clips = await api.ListClipsAsync(slug, ct);
    var clip = await api.GetClipAsync(clips.Clips[0].Id, ct);
    Console.WriteLine($"clip      {clip.Id} {clip.DurationSeconds}s {clip.ClipUrl}");
    var msgs = await api.GetChatWindowAsync(ch.Id, vod.StartTime.AddMinutes(10), ct);
    Console.WriteLine($"chat      {msgs.Count} messages in 5s window, first={msgs.FirstOrDefault()?.Sender?.Username}");
    return 0;
}

static int IntOpt(Dictionary<string, string> o, string key, int fallback) =>
    o.TryGetValue(key, out var v)
        ? int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : throw new ArgumentException($"--{key} must be an integer.")
        : fallback;

static double DoubleOpt(Dictionary<string, string> o, string key, double fallback) =>
    o.TryGetValue(key, out var v)
        ? double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : throw new ArgumentException($"--{key} must be a number.")
        : fallback;

static TimeSpan ParseTime(string text) =>
    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)
        ? TimeSpan.FromSeconds(sec)
        : TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var ts)
            ? ts
            : throw new ArgumentException($"Bad time value '{text}'.");

static Dictionary<string, string> ParseOptions(string[] rest, out List<string> positional)
{
    var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    positional = [];
    for (var i = 0; i < rest.Length; i++)
    {
        if (!rest[i].StartsWith("--", StringComparison.Ordinal))
        {
            positional.Add(rest[i]);
        }
        else if (i + 1 < rest.Length)
        {
            opts[rest[i][2..]] = rest[++i];
        }
        else
        {
            throw new ArgumentException($"Option {rest[i]} needs a value.");
        }
    }

    return opts;
}
