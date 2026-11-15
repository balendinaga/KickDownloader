using KickDownloaderCore.Api;

namespace KickDownloaderCore.Tests;

public sealed class KickApiClientTests
{
    private sealed class FakeTransport(Func<Uri, string> respond) : IKickTransport
    {
        public List<Uri> Requests { get; } = [];

        public Task<string> GetStringAsync(Uri uri, CancellationToken ct = default)
        {
            Requests.Add(uri);
            return Task.FromResult(respond(uri));
        }
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static (KickApiClient Client, FakeTransport Transport) Make(string fixture)
    {
        var t = new FakeTransport(_ => Fixture(fixture));
        return (new KickApiClient(t), t);
    }

    [Fact]
    public async Task GetChannel_parses_real_response()
    {
        var (c, t) = Make("channel.json");
        var ch = await c.GetChannelAsync("xqc");
        Assert.Equal(668, ch.Id);
        Assert.Equal("xqc", ch.Slug);
        Assert.True(ch.VodEnabled);
        Assert.Equal("https://kick.com/api/v2/channels/xqc", t.Requests.Single().ToString());
    }

    [Fact]
    public async Task ListVideos_exposes_uuid_and_start_time()
    {
        var (c, _) = Make("videos_list.json");
        var vods = await c.ListVideosAsync("xqc");
        Assert.Equal(23, vods.Count);
        Assert.Equal("371f0495-9aaa-4e7a-b88e-22b50c6194b0", vods[0].Video!.Uuid);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 22, 41, 47, TimeSpan.Zero), vods[0].StartTime);
        Assert.Equal(34_646_000, vods[0].DurationMs);
    }

    [Fact]
    public async Task GetVideo_returns_master_playlist_source()
    {
        var (c, t) = Make("video.json");
        var v = await c.GetVideoAsync("371f0495-9aaa-4e7a-b88e-22b50c6194b0");
        Assert.EndsWith("/media/hls/master.m3u8", v.Source);
        Assert.Equal(137883983, v.Livestream!.Id);
        Assert.Equal("https://kick.com/api/v1/video/371f0495-9aaa-4e7a-b88e-22b50c6194b0", t.Requests.Single().ToString());
    }

    [Fact]
    public async Task GetClip_unwraps_envelope()
    {
        var (c, _) = Make("clip.json");
        var clip = await c.GetClipAsync("clip_01M7G5HZCPJGB4125AC4RZJDA9");
        Assert.Equal(49, clip.DurationSeconds);
        Assert.EndsWith("/playlist.m3u8", clip.ClipUrl);
        Assert.Equal(38530, clip.VodStartsAtSeconds);
    }

    [Fact]
    public async Task ListClips_parses_cursor_and_items()
    {
        var (c, _) = Make("clips_list.json");
        var list = await c.ListClipsAsync("xqc");
        Assert.NotEmpty(list.Clips);
        Assert.False(string.IsNullOrEmpty(list.Clips[0].Id));
    }

    [Fact]
    public async Task GetChatWindow_formats_utc_start_time_and_parses_messages()
    {
        var (c, t) = Make("chat.json");
        var start = new DateTimeOffset(2026, 11, 14, 22, 41, 47, TimeSpan.FromHours(3));
        var msgs = await c.GetChatWindowAsync(668, start);
        Assert.Equal(2, msgs.Count);
        Assert.Equal("viewer1", msgs[0].Sender!.Username);
        Assert.Equal("#72ACED", msgs[0].Sender!.Identity!.Color);
        Assert.Equal("level", msgs[0].Sender!.Identity!.Badges[0].Name);
        Assert.Contains("start_time=2026-11-14T19%3A41%3A47.000Z", t.Requests.Single().Query);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../etc")]
    [InlineData("a/b")]
    [InlineData("x?y=1")]
    public async Task Invalid_slug_is_rejected_before_any_request(string slug)
    {
        var (c, t) = Make("channel.json");
        await Assert.ThrowsAsync<ArgumentException>(() => c.GetChannelAsync(slug));
        Assert.Empty(t.Requests);
    }

    [Fact]
    public async Task Invalid_uuid_and_clip_id_are_rejected()
    {
        var (c, t) = Make("video.json");
        await Assert.ThrowsAsync<ArgumentException>(() => c.GetVideoAsync("not-a-uuid"));
        await Assert.ThrowsAsync<ArgumentException>(() => c.GetClipAsync("clip_1/../x"));
        Assert.Empty(t.Requests);
    }

    [Fact]
    public async Task Wrong_shape_becomes_InvalidDataException()
    {
        var c = new KickApiClient(new FakeTransport(_ => "[1,2,3]"));
        await Assert.ThrowsAsync<InvalidDataException>(() => c.GetChannelAsync("xqc"));
    }

    [Fact]
    public async Task Forbidden_surfaces_as_KickBlockedException()
    {
        var handler = new StubHandler(System.Net.HttpStatusCode.Forbidden);
        var transport = new HttpClientTransport(new HttpClient(handler));
        await Assert.ThrowsAsync<KickBlockedException>(
            () => transport.GetStringAsync(new Uri("https://kick.com/api/v2/channels/xqc")));
    }

    private sealed class StubHandler(System.Net.HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code));
    }
}
