using System.Globalization;
using System.Net;
using System.Text.Json;
using KickDownloaderCore.Api;
using KickDownloaderCore.Chat;
using TwitchDownloaderCore.TwitchObjects;
using KickDownloaderCore.Models;

namespace KickDownloaderCore.Tests;

public sealed class ChatTests
{
    private static readonly DateTimeOffset VodStart = new(2026, 11, 14, 22, 41, 47, TimeSpan.Zero);
    private static readonly byte[] Png112 = MakePng(112, 112);

    private static string Fixture(string n) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", n));

    private static byte[] MakePng(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
        b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
        return b;
    }

    private static KickChatMessage Msg(string id, double offsetSec, string content = "hi",
        IReadOnlyList<KickBadge>? badges = null, IReadOnlyList<KickLegacyBadge>? legacy = null) => new()
    {
        Id = id,
        Content = content,
        Type = "message",
        CreatedAt = VodStart.AddSeconds(offsetSec),
        Sender = new KickSender
        {
            Id = 7,
            Slug = "bob",
            Username = "Bob",
            Identity = new KickIdentity { Color = "#FF0000", Badges = badges ?? [], LegacyBadges = legacy ?? [] },
        },
    };

    private sealed class FakeAssets(Func<Uri, byte[]?> respond) : IAssetFetcher
    {
        public Task<byte[]?> GetBytesAsync(Uri uri, CancellationToken ct = default) => Task.FromResult(respond(uri));
    }

    private static long Micros(DateTimeOffset t) => (t.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;

    /// <summary>
    /// Serves chat like Kick's cursor paging: messages before the cursor (Unix microseconds), newest first, at most
    /// <c>cap</c> per page, with the oldest message's time as the next cursor.
    /// </summary>
    private sealed class FakeKick(List<KickChatMessage> all, int cap = 25, bool frozenCursor = false) : IKickTransport
    {
        public List<long> Cursors { get; } = [];

        public Task<string> GetStringAsync(Uri uri, CancellationToken ct = default)
        {
            var path = uri.AbsolutePath;
            if (path.EndsWith("/messages"))
            {
                var before = long.Parse(uri.Query.Split("cursor=")[1], CultureInfo.InvariantCulture);
                Cursors.Add(before);
                var page = all.Where(m => Micros(m.CreatedAt) < before)
                    .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id, StringComparer.Ordinal).Take(cap).ToList();
                var next = page.Count == 0 ? null : (frozenCursor ? before : Micros(page[^1].CreatedAt)).ToString(CultureInfo.InvariantCulture);
                return Task.FromResult(JsonSerializer.Serialize(
                    new KickChatEnvelope { Data = new KickChatPage { Messages = page, Cursor = next } }));
            }

            return Task.FromResult(path.Contains("/v1/video/") ? Fixture("video.json") : Fixture("channel.json"));
        }
    }

    [Fact]
    public void Build_maps_message_to_twitch_schema()
    {
        var m = Msg("a", 12.5, "hello [emote:37226:KEKW] world [emote:99:Gone]",
            badges:
            [
                new KickBadge { Name = "level", ImageUrl = "https://ext.cdn.kick.com/l.png", Selected = true, Metadata = new() { Level = 30 } },
                new KickBadge { Name = "hidden", Selected = false },
            ],
            legacy: [new KickLegacyBadge { Type = "subscriber" }]);
        var assets = new ChatAssets(
            new Dictionary<string, EmoteAsset> { ["37226"] = new("KEKW", Png112, 112, 112) },
            new Dictionary<(string, string), BadgeAsset> { [("level", "30")] = new([1, 2, 3], "https://ext.cdn.kick.com/l.png") });
        var root = ChatConverter.Build(
            new KickChannel { Id = 668, Slug = "xqc", User = new KickUser { Username = "xQc" } },
            new KickVideo { Uuid = "u1", Livestream = new KickLivestream { StartTime = VodStart, DurationMs = 60_000, SessionTitle = "T" } },
            [m], assets, 0, 60, VodStart);

        var c = root.comments.Single();
        Assert.Equal(12.5, c.content_offset_seconds);
        Assert.Equal("668", c.channel_id);
        Assert.Equal("u1", c.content_id);
        Assert.Equal("Bob", c.commenter.display_name);
        Assert.Equal("7", c.commenter._id);
        Assert.Equal("#FF0000", c.message.user_color);
        Assert.Equal("hello KEKW world Gone", c.message.body);
        Assert.Equal(["hello ", "KEKW", " world Gone"], c.message.fragments.Select(f => f.text));
        Assert.Equal("37226", c.message.fragments[1].emoticon!.emoticon_id);
        Assert.Null(c.message.fragments[2].emoticon);
        var e = c.message.emoticons.Single();
        Assert.Equal("KEKW", c.message.body.Substring(e.begin, e.end - e.begin + 1));
        Assert.Equal([("level", "30"), ("subscriber", "1")], c.message.user_badges.Select(b => (b._id, b.version)));
        Assert.Equal(4, root.embeddedData.firstParty.Single().imageScale);
        Assert.Equal("level", root.embeddedData.twitchBadges.Single().name);
        Assert.Equal(new byte[] { 1, 2, 3 }, root.embeddedData.twitchBadges[0].versions["30"].bytes);
        var v = root.FileInfo.Version;
        Assert.Equal("1.4.0", $"{v.Major}.{v.Minor}.{v.Patch}");
    }

    [Fact]
    public void Build_skips_messages_before_vod_start()
    {
        var root = ChatConverter.Build(new KickChannel { Id = 1, Slug = "x" },
            new KickVideo { Uuid = "u", Livestream = new KickLivestream { StartTime = VodStart } },
            [Msg("early", -3), Msg("ok", 3)], ChatAssets.Empty, 0, 10, VodStart);
        Assert.Equal(["ok"], root.comments.Select(c => c._id));
    }

    [Fact]
    public void Json_uses_exact_twitchdownloader_property_names()
    {
        var root = ChatConverter.Build(new KickChannel { Id = 1, Slug = "x" },
            new KickVideo { Uuid = "u", Livestream = new KickLivestream { StartTime = VodStart } },
            [Msg("a", 1)], ChatAssets.Empty, 0, 10, VodStart);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(root));
        var r = doc.RootElement;
        foreach (var k in new[] { "FileInfo", "streamer", "clipper", "video", "comments", "embeddedData" })
        {
            Assert.True(r.TryGetProperty(k, out _), k);
        }

        var c = r.GetProperty("comments")[0];
        foreach (var k in new[] { "_id", "created_at", "channel_id", "content_type", "content_id", "content_offset_seconds", "commenter", "message" })
        {
            Assert.True(c.TryGetProperty(k, out _), k);
        }

        foreach (var k in new[] { "body", "bits_spent", "fragments", "user_badges", "user_color", "emoticons" })
        {
            Assert.True(c.GetProperty("message").TryGetProperty(k, out _), k);
        }

        Assert.False(c.GetProperty("message").TryGetProperty("user_notice_params", out _));
        foreach (var k in new[] { "thirdParty", "firstParty", "twitchBadges", "twitchBits" })
        {
            Assert.True(r.GetProperty("embeddedData").TryGetProperty(k, out _), k);
        }

        var ver = r.GetProperty("FileInfo").GetProperty("Version");
        Assert.Equal(1, ver.GetProperty("Major").GetInt32());
        Assert.Equal(4, ver.GetProperty("Minor").GetInt32());
    }

    [Theory]
    [InlineData(28, 1)]
    [InlineData(56, 2)]
    [InlineData(112, 4)]
    [InlineData(0, 2)]
    [InlineData(32, 1)]
    [InlineData(64, 2)]
    [InlineData(70, 3)]
    [InlineData(200, 7)]
    public void ImageScale_maps_height_to_twitch_scale(int h, int scale) => Assert.Equal(scale, ChatConverter.ImageScaleFor(h));

    [Fact]
    public void ImageInfo_reads_png_and_gif_headers()
    {
        Assert.Equal((112, 112), ImageInfo.GetSize(Png112));
        var gif = new byte[13];
        "GIF89a"u8.CopyTo(gif);
        gif[6] = 70;
        gif[8] = 70;
        Assert.Equal((70, 70), ImageInfo.GetSize(gif));
        Assert.Equal((0, 0), ImageInfo.GetSize([1, 2, 3]));
    }

    [Theory]
    [InlineData("https://files.kick.com/emotes/1/fullsize", true)]
    [InlineData("https://ext.cdn.kick.com/a.png", true)]
    [InlineData("http://files.kick.com/a.png", false)]
    [InlineData("https://kick.com.evil.io/a.png", false)]
    public void AssetFetcher_trusts_only_https_kick_hosts(string url, bool ok) =>
        Assert.Equal(ok, HttpAssetFetcher.IsTrusted(new Uri(url)));

    private static KickChatDownloader Downloader(FakeKick kick, Action<DateTimeOffset>? warn = null) =>
        new(new KickApiClient(kick), new FakeAssets(_ => null)) { OnPossibleTruncation = warn };

    [Fact]
    public async Task Range_collects_every_message_of_a_busy_second()
    {
        // 80 messages inside one second (a page holds only 25), plus quiet traffic around it.
        var all = Enumerable.Range(0, 80).Select(i => Msg($"burst{i:D3}", 100 + (i * 0.0125))).ToList();
        all.AddRange(Enumerable.Range(0, 40).Select(i => Msg($"q{i:D3}", 90 + (i * 0.5))));
        var warned = new List<DateTimeOffset>();
        var got = await Downloader(new FakeKick(all), warned.Add)
            .CollectRangeAsync(668, VodStart.AddSeconds(90), VodStart.AddSeconds(110));

        Assert.Equal(all.Count, got.Count);
        Assert.Equal(all.Select(m => m.Id).Order(), got.Select(m => m.Id).Order());
        Assert.Empty(warned);
        Assert.True(got.Zip(got.Skip(1)).All(p => p.First.CreatedAt <= p.Second.CreatedAt), "oldest first");
    }

    [Fact]
    public async Task Range_keeps_only_messages_inside_and_stops_after_start()
    {
        var all = Enumerable.Range(0, 200).Select(i => Msg($"m{i:D3}", i)).ToList(); // one per second, 0..199 s
        var kick = new FakeKick(all, cap: 25);
        var got = await Downloader(kick).CollectRangeAsync(668, VodStart.AddSeconds(100), VodStart.AddSeconds(150));

        Assert.Equal(Enumerable.Range(100, 50).Select(i => $"m{i:D3}"), got.Select(m => m.Id));
        Assert.Equal(3, kick.Cursors.Count); // 149..125, 124..100, 99..75 (older than start: stop)
    }

    [Fact]
    public async Task Range_with_no_messages_costs_one_request()
    {
        var kick = new FakeKick([]);
        Assert.Empty(await Downloader(kick).CollectRangeAsync(668, VodStart, VodStart.AddSeconds(60)));
        Assert.Single(kick.Cursors);
    }

    [Fact]
    public async Task Range_reports_progress_up_to_one()
    {
        var all = Enumerable.Range(0, 100).Select(i => Msg($"m{i:D3}", i)).ToList();
        var reports = new List<double>();
        await Downloader(new FakeKick(all, cap: 10)).CollectRangeAsync(
            668, VodStart, VodStart.AddSeconds(100), new Progress<double>(reports.Add));
        await Task.Delay(50);
        Assert.Equal(1.0, reports.Max());
        Assert.All(reports, r => Assert.InRange(r, 0, 1));
    }

    [Fact]
    public async Task Range_terminates_and_warns_when_the_cursor_does_not_advance()
    {
        var all = Enumerable.Range(0, 60).Select(i => Msg($"m{i:D3}", 100)).ToList();
        var warned = new List<DateTimeOffset>();
        var kick = new FakeKick(all, cap: 25, frozenCursor: true);
        var got = await Downloader(kick, warned.Add).CollectRangeAsync(668, VodStart.AddSeconds(50), VodStart.AddSeconds(120));
        Assert.NotEmpty(warned);
        Assert.InRange(kick.Cursors.Count, 1, 3);
        Assert.True(got.Count > 0);
    }

    [Fact]
    public async Task Range_cancellation_is_honored()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader(new FakeKick([Msg("a", 1)]))
            .CollectRangeAsync(668, VodStart, VodStart.AddSeconds(10), null, cts.Token));
    }

    [Fact]
    public async Task Download_keeps_all_messages_of_a_dense_second()
    {
        var all = Enumerable.Range(0, 90).Select(i => Msg($"d{i:D3}", 20 + (i * 0.01))).ToList();
        var root = await Downloader(new FakeKick(all)).DownloadAsync("371f0495-9aaa-4e7a-b88e-22b50c6194b0", "xqc",
            new ChatDownloadOptions { Duration = TimeSpan.FromSeconds(60) });
        Assert.Equal(90, root.comments.Count);
    }

    [Fact]
    public async Task Download_filters_range_dedupes_and_tolerates_failed_assets()
    {
        var all = new List<KickChatMessage>
        {
            Msg("a", 2, "x [emote:5:Pog]"), Msg("b", 7, "y"), Msg("c", 8, "z"), Msg("out", 60, "late"),
        };
        var fetched = new List<Uri>();
        var assets = new FakeAssets(u => { fetched.Add(u); return null; });
        var dl = new KickChatDownloader(new KickApiClient(new FakeKick(all)), assets);
        var root = await dl.DownloadAsync("371f0495-9aaa-4e7a-b88e-22b50c6194b0", "xqc",
            new ChatDownloadOptions { Start = TimeSpan.Zero, Duration = TimeSpan.FromSeconds(30) });
        Assert.Equal(["a", "b", "c"], root.comments.Select(c => c._id));
        Assert.Equal("x Pog", root.comments[0].message.body);
        Assert.Empty(root.embeddedData.firstParty);
        Assert.Contains(fetched, u => u.AbsoluteUri == "https://files.kick.com/emotes/5/fullsize");
        Assert.Equal(30, root.video.end);
    }

    [Fact]
    public async Task Download_derives_channel_from_vod_when_slug_is_missing()
    {
        var dl = new KickChatDownloader(new KickApiClient(new FakeKick([Msg("a", 2)])), new FakeAssets(_ => null));
        var root = await dl.DownloadAsync("371f0495-9aaa-4e7a-b88e-22b50c6194b0", null,
            new ChatDownloadOptions { Duration = TimeSpan.FromSeconds(10) });
        Assert.Equal("xqc", root.streamer.login);
        Assert.Single(root.comments);
    }

    [Fact]
    public async Task Download_rejects_bad_range()
    {
        var dl = new KickChatDownloader(new KickApiClient(new FakeKick([])), new FakeAssets(_ => null));
        await Assert.ThrowsAsync<ArgumentException>(() => dl.DownloadAsync("371f0495-9aaa-4e7a-b88e-22b50c6194b0", "xqc",
            new ChatDownloadOptions { Start = TimeSpan.FromDays(2) }));
        await Assert.ThrowsAsync<ArgumentException>(() => dl.DownloadAsync("371f0495-9aaa-4e7a-b88e-22b50c6194b0", "xqc",
            new ChatDownloadOptions { Start = TimeSpan.FromSeconds(-1) }));
    }

    private sealed class SequenceHandler(params HttpStatusCode[] codes) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var code = codes[Math.Min(Calls++, codes.Length - 1)];
            var resp = new HttpResponseMessage(code) { Content = new StringContent("{}") };
            if (code == HttpStatusCode.TooManyRequests)
            {
                resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
            }

            return Task.FromResult(resp);
        }
    }

    [Fact]
    public async Task Transport_backs_off_5_15_30_on_consecutive_429()
    {
        var delays = new List<TimeSpan>();
        var h = new SequenceHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var notices = new List<RateLimitNotice>();
        var t = new HttpClientTransport(new HttpClient(h),
            new TransportOptions(TimeSpan.Zero, 8, (s, _) => { delays.Add(s); return Task.CompletedTask; }));
        t.RateLimited += notices.Add;
        Assert.Equal("{}", await t.GetStringAsync(new Uri("https://kick.com/api/x")));
        Assert.Equal(4, h.Calls);
        Assert.Equal(50, delays.Sum(d => d.TotalSeconds)); // 5 + 15 + 30; Retry-After 3 s is shorter, so ignored
        Assert.Equal(3, notices.Count(n => n.SecondsLeft == 0));
        Assert.Contains(notices, n => n.SecondsLeft == 30 && n.Attempt == 3);
    }

    [Fact]
    public async Task Transport_honors_longer_retry_after()
    {
        var delays = new List<TimeSpan>();
        var h = new LongRetryAfterHandler();
        var t = new HttpClientTransport(new HttpClient(h),
            new TransportOptions(TimeSpan.Zero, 3, (s, _) => { delays.Add(s); return Task.CompletedTask; }));
        await t.GetStringAsync(new Uri("https://kick.com/api/x"));
        Assert.Equal(40, delays.Sum(d => d.TotalSeconds));
    }

    private sealed class LongRetryAfterHandler : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var resp = new HttpResponseMessage(_calls++ == 0 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            };
            resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(40));
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public void Default_transport_spaces_requests_at_least_350ms_and_retries_long()
    {
        Assert.InRange(TransportOptions.Default.MinInterval.TotalMilliseconds, 350, 450);
        Assert.True(TransportOptions.Default.MaxRetries >= 8);
    }

    [Fact]
    public async Task Transport_cancel_during_rate_limit_wait_stops_cleanly()
    {
        using var cts = new CancellationTokenSource();
        var h = new SequenceHandler(HttpStatusCode.TooManyRequests);
        var t = new HttpClientTransport(new HttpClient(h),
            new TransportOptions(TimeSpan.Zero, 8, (_, _) => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t.GetStringAsync(new Uri("https://kick.com/api/x"), cts.Token));
    }

    [Fact]
    public async Task Transport_gives_up_after_max_retries()
    {
        var h = new SequenceHandler(HttpStatusCode.TooManyRequests);
        var t = new HttpClientTransport(new HttpClient(h), new TransportOptions(TimeSpan.Zero, 2, (_, _) => Task.CompletedTask));
        await Assert.ThrowsAsync<HttpRequestException>(() => t.GetStringAsync(new Uri("https://kick.com/api/x")));
        Assert.Equal(3, h.Calls);
    }

    [Fact]
    public async Task Transport_spaces_requests_by_min_interval()
    {
        var delays = new List<TimeSpan>();
        var h = new SequenceHandler(HttpStatusCode.OK);
        var t = new HttpClientTransport(new HttpClient(h),
            new TransportOptions(TimeSpan.FromSeconds(10), 1, (s, _) => { delays.Add(s); return Task.CompletedTask; }));
        await t.GetStringAsync(new Uri("https://kick.com/api/x"));
        await t.GetStringAsync(new Uri("https://kick.com/api/x"));
        Assert.Single(delays);
        Assert.InRange(delays[0], TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10));
    }
}
