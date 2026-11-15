using KickDownloaderCore.Api;

namespace KickDownloaderCore.Tests;

public sealed class InputTests : IDisposable
{
    private const string Uuid = "371f0495-9aaa-4e7a-b88e-22b50c6194b0";
    private const string Clip = "clip_01M7G5HZCPJGB4125AC4RZJDA9";
    private readonly string _dir = Directory.CreateTempSubdirectory("kd-input-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(Uuid)]
    [InlineData("  " + Uuid + "  ")]
    [InlineData("https://kick.com/xqc/videos/" + Uuid)]
    [InlineData("https://www.kick.com/xqc/videos/" + Uuid + "?t=10")]
    [InlineData("kick.com/xqc/videos/" + Uuid)]
    public void Vod_is_extracted_from_ids_and_links(string input)
    {
        Assert.True(KickInput.TryParseVod(input, out var id));
        Assert.Equal(Uuid, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a uuid")]
    [InlineData(Uuid + "-extra")]
    [InlineData("https://evil.example.com/xqc/videos/" + Uuid)]
    [InlineData("https://kick.com.evil.io/xqc/videos/" + Uuid)]
    [InlineData("ftp://kick.com/xqc/videos/" + Uuid)]
    [InlineData("https://kick.com/xqc/videos/")]
    public void Vod_rejects_junk_and_foreign_hosts(string input) => Assert.False(KickInput.TryParseVod(input, out _));

    [Theory]
    [InlineData(Clip)]
    [InlineData("https://kick.com/xqc/clips/" + Clip)]
    [InlineData("https://kick.com/xqc?clip=" + Clip)]
    [InlineData("kick.com/xqc/clips/" + Clip + "?x=1")]
    public void Clip_is_extracted_from_ids_and_links(string input)
    {
        Assert.True(KickInput.TryParseClip(input, out var id));
        Assert.Equal(Clip, id);
    }

    [Theory]
    [InlineData(Uuid)]
    [InlineData("https://evil.example.com/clips/" + Clip)]
    [InlineData("clip_")]
    [InlineData("")]
    public void Clip_rejects_junk(string input) => Assert.False(KickInput.TryParseClip(input, out _));

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:30:00", 5400)]
    [InlineData("1:30:00.5", 5400.5)]
    [InlineData(" 0 ", 0)]
    public void Time_formats_are_parsed(string input, double seconds)
    {
        Assert.True(TimeInput.TryParse(input, out var t));
        Assert.Equal(seconds, t.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1:75")]
    [InlineData("00:61:00")]
    [InlineData("1:2:3:4")]
    [InlineData("99999999")]
    [InlineData("1:-5")]
    public void Time_rejects_bad_values(string input) => Assert.False(TimeInput.TryParse(input, out _));

    [Fact]
    public void Time_formats_as_hh_mm_ss() => Assert.Equal("01:01:01", TimeInput.Format(TimeSpan.FromSeconds(3661)));

    [Fact]
    public void Sanitize_replaces_invalid_characters_and_caps_length()
    {
        Assert.Equal("a_b_c_", OutputNaming.Sanitize("a/b:c?"));
        Assert.Equal("kick", OutputNaming.Sanitize("  ..  "));
        Assert.Equal(80, OutputNaming.Sanitize(new string('x', 200)).Length);
    }

    [Fact]
    public void UniquePath_never_overwrites()
    {
        var first = OutputNaming.UniquePath(_dir, "vod", ".mp4");
        Assert.Equal(Path.Combine(_dir, "vod.mp4"), first);
        File.WriteAllText(first, "x");
        var second = OutputNaming.UniquePath(_dir, "vod", ".mp4");
        Assert.Equal(Path.Combine(_dir, "vod (2).mp4"), second);
        File.WriteAllText(second, "x");
        Assert.Equal(Path.Combine(_dir, "vod (3).mp4"), OutputNaming.UniquePath(_dir, "vod", ".mp4"));
    }
}
