using System.Diagnostics;
using KickDownloaderCore.Chat;
using KickDownloaderCore.Models;
using KickDownloaderCore.Rendering;

namespace KickDownloaderCore.Tests;

public sealed class RenderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kd-render-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Input()
    {
        var path = Path.Combine(_dir, "chat.json");
        File.WriteAllText(path, "{}");
        return path;
    }

    private RenderSettings Settings(Func<RenderSettings, RenderSettings>? tweak = null)
    {
        var s = new RenderSettings { InputFile = Input(), OutputFile = Path.Combine(_dir, "out.mov") };
        return tweak?.Invoke(s) ?? s;
    }

    [Fact]
    public void BuildOptions_maps_settings_and_forces_offline_kick_mode()
    {
        var o = ChatRenderService.BuildOptions(Settings(s => s with { Width = 400, Height = 800, Fps = 60, FontSize = 18 }));
        Assert.Equal(400, o.ChatWidth);
        Assert.Equal(800, o.ChatHeight);
        Assert.Equal(60, o.Framerate);
        Assert.Equal(18, o.FontSize);
        Assert.True(o.Offline);
        Assert.False(o.BttvEmotes || o.FfzEmotes || o.StvEmotes);
        Assert.Equal(0, o.BackgroundColor.Alpha);
        Assert.Contains("prores_ks", o.OutputArgs);
        Assert.Contains("{save_path}", o.OutputArgs);
        Assert.Equal(-1, o.StartOverride);
    }

    [Theory]
    [InlineData(RenderFormat.MovProRes, "yuva444p10le", ".mov")]
    [InlineData(RenderFormat.WebmVp9, "yuva420p", ".webm")]
    [InlineData(RenderFormat.Mp4, "yuv420p", ".mp4")]
    public void Format_selects_alpha_pixel_format_and_extension(RenderFormat f, string pixFmt, string ext)
    {
        Assert.Contains($"-pix_fmt {pixFmt}", ChatRenderService.OutputArgsFor(f));
        Assert.Equal(ext, ChatRenderService.ExpectedExtension(f));
    }

    [Fact]
    public void Trim_range_becomes_whole_second_overrides()
    {
        var o = ChatRenderService.BuildOptions(Settings(s => s with { Start = TimeSpan.FromSeconds(10.9), End = TimeSpan.FromMinutes(1) }));
        Assert.Equal(10, o.StartOverride);
        Assert.Equal(60, o.EndOverride);
    }

    [Theory]
    [InlineData(351, 600)]
    [InlineData(350, 601)]
    [InlineData(50, 600)]
    [InlineData(350, 9000)]
    public void Odd_or_out_of_range_size_is_rejected(int w, int h) =>
        Assert.Throws<ArgumentException>(() => ChatRenderService.BuildOptions(Settings(s => s with { Width = w, Height = h })));

    [Fact]
    public void Bad_fps_color_rate_and_extension_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ChatRenderService.BuildOptions(Settings(s => s with { Fps = 0 })));
        Assert.Throws<ArgumentException>(() => ChatRenderService.BuildOptions(Settings(s => s with { Fps = 500 })));
        Assert.Throws<ArgumentException>(() => ChatRenderService.BuildOptions(Settings(s => s with { BackgroundColor = "banana" })));
        Assert.Throws<ArgumentException>(() => ChatRenderService.BuildOptions(Settings(s => s with { UpdateRate = 0 })));
        Assert.Throws<ArgumentException>(() => ChatRenderService.BuildOptions(Settings(s => s with { Format = RenderFormat.Mp4 })));
    }

    [Fact]
    public void Missing_input_and_existing_output_are_rejected()
    {
        Assert.Throws<FileNotFoundException>(() => ChatRenderService.BuildOptions(Settings(s => s with { InputFile = Path.Combine(_dir, "nope.json") })));
        var existing = Path.Combine(_dir, "taken.mov");
        File.WriteAllText(existing, "x");
        Assert.Throws<IOException>(() => ChatRenderService.BuildOptions(Settings(s => s with { OutputFile = existing })));
    }

    private static bool FfmpegAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("ffmpeg", "-version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            p!.WaitForExit(10_000);
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>End-to-end smoke test of the vendored renderer: needs ffmpeg on PATH, otherwise it returns early.</summary>
    [Fact]
    public async Task Render_smoke_produces_a_decodable_mp4()
    {
        if (!FfmpegAvailable())
        {
            return;
        }

        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var msg = new KickChatMessage
        {
            Id = "m1",
            Content = "hello world",
            Type = "message",
            CreatedAt = start.AddSeconds(1),
            Sender = new KickSender { Id = 1, Slug = "a", Username = "Alice", Identity = new KickIdentity { Color = "#33AAFF" } },
        };
        var root = ChatConverter.Build(
            new KickChannel { Id = 5, Slug = "x" },
            new KickVideo { Uuid = "u", Livestream = new KickLivestream { StartTime = start, DurationMs = 4000 } },
            [msg], ChatAssets.Empty, 0, 3, start);
        var json = Path.Combine(_dir, "smoke.json");
        await KickChatDownloader.WriteAsync(root, json);

        var output = Path.Combine(_dir, "smoke.mp4");
        await ChatRenderService.RenderAsync(
            new RenderSettings { InputFile = json, OutputFile = output, Width = 200, Height = 200, Fps = 10, Format = RenderFormat.Mp4 },
            new ConsoleTaskProgress());

        Assert.True(new FileInfo(output).Length > 1000);
        using var probe = Process.Start(new ProcessStartInfo("ffmpeg", $"-v error -i \"{output}\" -f null -")
            { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
        var err = await probe.StandardError.ReadToEndAsync();
        await probe.WaitForExitAsync();
        Assert.Equal(0, probe.ExitCode);
        Assert.Equal("", err.Trim());
    }
}
