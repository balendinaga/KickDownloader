using KickDownloaderCore.Api;
using KickDownloaderCore.Video;

namespace KickDownloaderCore.Tests;

public sealed class VideoTests
{
    private static readonly Uri Master = new("https://stream.kick.com/a/b/media/hls/master.m3u8");

    private static string Fixture(string n) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", n));

    private sealed class FakeFfmpeg : IFfmpegRunner
    {
        public List<string> Args { get; private set; } = [];

        public Task RunAsync(IReadOnlyList<string> args, CancellationToken ct = default, IProgress<TimeSpan>? progress = null)
        {
            Args = [.. args];
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTransport(Func<Uri, string> respond) : IKickTransport
    {
        public Task<string> GetStringAsync(Uri uri, CancellationToken ct = default) => Task.FromResult(respond(uri));
    }

    [Fact]
    public void Parse_reads_all_five_variants_with_resolved_urls()
    {
        var v = HlsMaster.Parse(Fixture("master.m3u8"), Master);
        Assert.Equal(5, v.Count);
        Assert.Equal("1080p60", v[0].Name);
        Assert.Equal(9068556, v[0].Bandwidth);
        Assert.Equal("1920x1080", v[0].Resolution);
        Assert.Equal("https://stream.kick.com/a/b/media/hls/1080p60/playlist.m3u8", v[0].Url.AbsoluteUri);
    }

    [Fact]
    public void Select_defaults_to_best_and_matches_prefix()
    {
        var v = HlsMaster.Parse(Fixture("master.m3u8"), Master);
        Assert.Equal("1080p60", HlsMaster.Select(v, null).Name);
        Assert.Equal("480p30", HlsMaster.Select(v, "480p").Name);
        Assert.Equal("720p60", HlsMaster.Select(v, "720P60").Name);
        Assert.Throws<ArgumentException>(() => HlsMaster.Select(v, "999p"));
    }

    [Fact]
    public void Select_throws_on_empty_playlist() =>
        Assert.Throws<InvalidDataException>(() => HlsMaster.Select([], null));

    [Fact]
    public async Task Vod_builds_seek_duration_and_copy_args()
    {
        var ff = new FakeFfmpeg();
        var t = new FakeTransport(u => u.AbsolutePath.EndsWith("master.m3u8") ? Fixture("master.m3u8") : Fixture("video.json"));
        var dl = new VodDownloader(new KickApiClient(t), t, ff);
        var path = Path.Combine(Path.GetTempPath(), $"kd-{Guid.NewGuid():N}.mp4");

        await dl.DownloadAsync("371f0495-9aaa-4e7a-b88e-22b50c6194b0", path,
            TimeSpan.FromSeconds(3600), TimeSpan.FromSeconds(20), "480p");

        var a = ff.Args;
        Assert.Equal("3600", a[a.IndexOf("-ss") + 1]);
        Assert.Equal("20", a[a.IndexOf("-t") + 1]);
        Assert.EndsWith("/480p30/playlist.m3u8", a[a.IndexOf("-i") + 1]);
        Assert.True(a.IndexOf("-ss") < a.IndexOf("-i") && a.IndexOf("-i") < a.IndexOf("-t"));
        Assert.Equal(path, a[^1]);
        Assert.Contains("-c", a);
    }

    [Fact]
    public async Task Vod_rejects_existing_output_and_bad_range()
    {
        var ff = new FakeFfmpeg();
        var t = new FakeTransport(_ => Fixture("video.json"));
        var dl = new VodDownloader(new KickApiClient(t), t, ff);
        var existing = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                dl.DownloadAsync("371f0495-9aaa-4e7a-b88e-22b50c6194b0", existing));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                dl.DownloadAsync("371f0495-9aaa-4e7a-b88e-22b50c6194b0", existing + "x", TimeSpan.FromSeconds(-1)));
            Assert.Empty(ff.Args);
        }
        finally { File.Delete(existing); }
    }

    [Fact]
    public async Task Clip_passes_trusted_url_to_ffmpeg()
    {
        var ff = new FakeFfmpeg();
        var dl = new ClipDownloader(new KickApiClient(new FakeTransport(_ => Fixture("clip.json"))), ff);
        var path = Path.Combine(Path.GetTempPath(), $"kd-{Guid.NewGuid():N}.mp4");
        await dl.DownloadAsync("clip_01M7G5HZCPJGB4125AC4RZJDA9", path);
        Assert.Contains(ff.Args, x => x.StartsWith("https://clips.kick.com/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://clips.kick.com/x.m3u8")]
    [InlineData("https://evil.example.com/x.m3u8")]
    [InlineData("https://kick.com.evil.io/x.m3u8")]
    [InlineData("file:///c:/secret")]
    public async Task Clip_refuses_untrusted_media_urls(string url)
    {
        var json = Fixture("clip.json").Replace("https://clips.kick.com/clips/02/clip_01M7G5HZCPJGB4125AC4RZJDA9/playlist.m3u8", url);
        var ff = new FakeFfmpeg();
        var dl = new ClipDownloader(new KickApiClient(new FakeTransport(_ => json)), ff);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            dl.DownloadAsync("clip_01M7G5HZCPJGB4125AC4RZJDA9", Path.Combine(Path.GetTempPath(), $"kd-{Guid.NewGuid():N}.mp4")));
        Assert.Empty(ff.Args);
    }
}
