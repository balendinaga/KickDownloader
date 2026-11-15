using KickDownloaderCore.Api;
using KickDownloaderCore.Chat;
using KickDownloaderCore.Video;

namespace KickDownloaderCore.Tests;

/// <summary>UUIDv7 VOD links (kick.com/{channel}/videos/{uuidv7}) only resolve through web.kick.com with the channel id.</summary>
public sealed class VodRefTests
{
    private const string V7 = "01a1d181-ff00-7f71-8638-3a34ea90175b";
    private const string V4 = "371f0495-9aaa-4e7a-b88e-22b50c6194b0";
    private const string ChannelJson = """{"id":26239555,"slug":"samplestreamer","user_id":27259381,"vod_enabled":true}""";

    private static string Fixture(string n) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", n));

    private sealed class FakeKick(Func<Uri, string?> respond) : IKickTransport
    {
        public List<Uri> Requests { get; } = [];

        public Task<string> GetStringAsync(Uri uri, CancellationToken ct = default)
        {
            Requests.Add(uri);
            return Task.FromResult(respond(uri) ?? throw new InvalidOperationException($"unexpected request {uri}"));
        }
    }

    private static FakeKick Web(string? videoJson = null) => new(u => u.AbsolutePath switch
    {
        "/api/v2/channels/samplestreamer" => ChannelJson,
        var p when p.StartsWith("/api/v1/channels/26239555/videos/") => videoJson ?? Fixture("web_video.json"),
        var p when p.EndsWith("master.m3u8") => Fixture("master.m3u8"),
        var p when p.EndsWith("/messages") => """{"data":{"messages":[]}}""",
        _ => null,
    });

    [Fact]
    public void IsV7_tells_the_two_id_families_apart()
    {
        Assert.True(new KickVodRef(V7).IsV7);
        Assert.False(new KickVodRef(V4).IsV7);
    }

    [Theory]
    [InlineData("https://kick.com/samplestreamer/videos/" + V7, "samplestreamer")]
    [InlineData("kick.com/samplestreamer/videos/" + V7 + "?t=30", "samplestreamer")]
    [InlineData("https://kick.com/xqc/videos/" + V4, "xqc")]
    [InlineData(V7, null)]
    [InlineData("https://kick.com/some_channel-1/videos/" + V7, "some_channel-1")]
    [InlineData("https://kick.com/video/" + V7, null)]
    public void Link_yields_uuid_and_channel(string input, string? slug)
    {
        Assert.True(KickInput.TryParseVodRef(input, out var vod));
        Assert.Equal(input.Contains(V7) ? V7 : V4, vod.Uuid);
        Assert.Equal(slug, vod.ChannelSlug);
    }

    [Theory]
    [InlineData("https://evil.example.com/samplestreamer/videos/" + V7)]
    [InlineData("not a vod")]
    [InlineData("")]
    public void Foreign_or_junk_input_has_no_ref(string input) => Assert.False(KickInput.TryParseVodRef(input, out _));

    [Fact]
    public async Task V7_is_looked_up_through_the_web_api_with_the_channel_id()
    {
        var kick = Web();
        var video = await new KickApiClient(kick).GetVideoAsync(new KickVodRef(V7, "samplestreamer"));

        Assert.Equal(
            ["/api/v2/channels/samplestreamer", $"/api/v1/channels/26239555/videos/{V7}"],
            kick.Requests.Select(r => r.AbsolutePath));
        Assert.Equal("web.kick.com", kick.Requests[1].Host);
        Assert.Equal(V7, video.Uuid);
        Assert.EndsWith("/media/hls/master.m3u8", video.Source);
        var live = video.Livestream!;
        Assert.Equal(26239555, live.ChannelId);
        Assert.Equal("samplestreamer", live.Channel!.Slug);
        Assert.Equal(4_447_000, live.DurationMs);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 20, 45, 20, TimeSpan.Zero), live.StartTime);
        Assert.Equal(186478, video.Views);
    }

    [Fact]
    public async Task V7_without_a_channel_is_refused_before_any_request()
    {
        var kick = Web();
        await Assert.ThrowsAsync<KickVodNeedsChannelException>(() => new KickApiClient(kick).GetVideoAsync(new KickVodRef(V7)));
        Assert.Empty(kick.Requests);
    }

    [Fact]
    public async Task V4_still_uses_the_legacy_endpoint()
    {
        var kick = new FakeKick(_ => Fixture("video.json"));
        await new KickApiClient(kick).GetVideoAsync(V4);
        Assert.Equal($"https://kick.com/api/v1/video/{V4}", kick.Requests.Single().AbsoluteUri);
    }

    [Fact]
    public async Task V7_without_a_recording_is_reported_clearly()
    {
        var noRecording = Fixture("web_video.json").Replace("\"recording_url\"", "\"recording_url_gone\"");
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => new KickApiClient(Web(noRecording)).GetVideoAsync(new KickVodRef(V7, "samplestreamer")));
        Assert.Contains("no recording", ex.Message);
    }

    [Fact]
    public async Task Vod_info_lists_qualities_for_a_v7_vod()
    {
        var kick = Web();
        var info = await new VodDownloader(new KickApiClient(kick), kick, new NullFfmpeg())
            .GetInfoAsync(new KickVodRef(V7, "samplestreamer"));
        Assert.Equal("samplestreamer", info.ChannelSlug);
        Assert.Equal(TimeSpan.FromSeconds(4447), info.Duration);
        Assert.Equal(5, info.Variants.Count);
    }

    [Fact]
    public async Task Chat_download_works_for_a_v7_vod()
    {
        var kick = Web();
        var dl = new KickChatDownloader(new KickApiClient(kick), new NoAssets());
        var root = await dl.DownloadAsync(V7, "samplestreamer", new ChatDownloadOptions { Duration = TimeSpan.FromSeconds(10) });
        Assert.Equal("samplestreamer", root.streamer.login);
        Assert.Equal(26239555, root.streamer.id);
        Assert.Equal(4447, root.video.length);
        Assert.Equal(V7, root.video.id);
    }

    [Fact]
    public async Task Chat_download_for_v7_without_a_channel_asks_for_it()
    {
        var dl = new KickChatDownloader(new KickApiClient(Web()), new NoAssets());
        await Assert.ThrowsAsync<KickVodNeedsChannelException>(() => dl.DownloadAsync(V7, null));
    }

    private sealed class NullFfmpeg : IFfmpegRunner
    {
        public Task RunAsync(IReadOnlyList<string> args, CancellationToken ct = default, IProgress<TimeSpan>? progress = null) =>
            Task.CompletedTask;
    }

    private sealed class NoAssets : IAssetFetcher
    {
        public Task<byte[]?> GetBytesAsync(Uri uri, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
    }
}
