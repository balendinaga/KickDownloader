using System.Runtime.CompilerServices;
using SkiaSharp;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderCore.Options;

namespace KickDownloaderCore.Rendering;

public enum RenderFormat
{
    /// <summary>ProRes 4444 with alpha (.mov). Best quality, large files; opens in most editors.</summary>
    MovProRes,

    /// <summary>VP9 with alpha (.webm). Small files; OBS and browsers.</summary>
    WebmVp9,

    /// <summary>H.264 (.mp4). No alpha channel.</summary>
    Mp4,
}

public sealed record RenderSettings
{
    public required string InputFile { get; init; }
    public required string OutputFile { get; init; }
    public int Width { get; init; } = 350;
    public int Height { get; init; } = 600;
    public int Fps { get; init; } = 30;
    public RenderFormat Format { get; init; } = RenderFormat.MovProRes;

    /// <summary>#RRGGBB or #AARRGGBB. The default is fully transparent.</summary>
    public string BackgroundColor { get; init; } = "#00000000";
    public string MessageColor { get; init; } = "#ffffff";
    public double FontSize { get; init; } = 12;
    public double UpdateRate { get; init; } = 0.2;
    public bool Timestamp { get; init; }
    public bool Outline { get; init; }
    public double EmoteScale { get; init; } = 1.0;
    public TimeSpan? Start { get; init; }
    public TimeSpan? End { get; init; }
    public string FfmpegPath { get; init; } = "ffmpeg";
    public string? TempFolder { get; init; }
}

public static class ChatRenderService
{
    private const string InputArgs =
        "-framerate {fps} -f rawvideo -analyzeduration {max_int} -probesize {max_int} -pix_fmt {pix_fmt} -video_size {width}x{height} -i -";

    public static string OutputArgsFor(RenderFormat format) => format switch
    {
        RenderFormat.MovProRes => "-c:v prores_ks -profile:v 4444 -pix_fmt yuva444p10le \"{save_path}\"",
        RenderFormat.WebmVp9 => "-c:v libvpx-vp9 -pix_fmt yuva420p -auto-alt-ref 0 -b:v 0 -crf 24 \"{save_path}\"",
        RenderFormat.Mp4 => "-c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p \"{save_path}\"",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static string ExpectedExtension(RenderFormat format) => format switch
    {
        RenderFormat.MovProRes => ".mov",
        RenderFormat.WebmVp9 => ".webm",
        _ => ".mp4",
    };

    /// <summary>Validates the settings and maps them to the vendored renderer's options. Offline: Kick data is fully embedded.</summary>
    public static ChatRenderOptions BuildOptions(RenderSettings s)
    {
        if (s.Width is < 100 or > 8192 || s.Height is < 100 or > 8192 || s.Width % 2 != 0 || s.Height % 2 != 0)
        {
            throw new ArgumentException("Width and height must be even numbers between 100 and 8192.");
        }

        if (s.Fps is < 1 or > 120)
        {
            throw new ArgumentException("FPS must be between 1 and 120.");
        }

        if (s.UpdateRate <= 0 || s.FontSize < 4 || s.EmoteScale <= 0)
        {
            throw new ArgumentException("Update rate, font size and emote scale must be positive (font size >= 4).");
        }

        if (!string.Equals(Path.GetExtension(s.OutputFile), ExpectedExtension(s.Format), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Output file for format {s.Format} must end with {ExpectedExtension(s.Format)}.");
        }

        if (!File.Exists(s.InputFile))
        {
            throw new FileNotFoundException("Chat json not found.", s.InputFile);
        }

        if (File.Exists(s.OutputFile))
        {
            throw new IOException($"Output file already exists: {s.OutputFile}");
        }

        if (!SKColor.TryParse(s.BackgroundColor, out var background) || !SKColor.TryParse(s.MessageColor, out var message))
        {
            throw new ArgumentException("Colors must be #RRGGBB or #AARRGGBB.");
        }

        var alphaFormat = s.Format != RenderFormat.Mp4;
        if (alphaFormat && background.Alpha == 255)
        {
            // Not an error: an opaque background in an alpha format is legal, but almost never intended.
            Console.Error.WriteLine("warning: opaque background with an alpha format; use --background '#00000000' for transparency.");
        }

        return new ChatRenderOptions
        {
            InputFile = s.InputFile,
            OutputFile = s.OutputFile,
            BackgroundColor = background,
            AlternateBackgroundColor = background,
            HighlightUserColor = new SKColor(0xFF, 0x00, 0x59, 0xC8),
            MessageColor = message,
            ChatWidth = s.Width,
            ChatHeight = s.Height,
            StartOverride = s.Start is { } a ? (int)a.TotalSeconds : -1,
            EndOverride = s.End is { } b ? (int)b.TotalSeconds : -1,
            BttvEmotes = false,
            FfzEmotes = false,
            StvEmotes = false,
            AllowUnlistedEmotes = false,
            Outline = s.Outline,
            OutlineSize = 4,
            Font = "Inter Embedded",
            FontSize = s.FontSize,
            MessageFontStyle = SKFontStyle.Normal,
            UsernameFontStyle = SKFontStyle.Bold,
            UpdateRate = s.UpdateRate,
            Framerate = s.Fps,
            GenerateMask = false,
            InputArgs = InputArgs,
            OutputArgs = OutputArgsFor(s.Format),
            FfmpegPath = s.FfmpegPath,
            TempFolder = s.TempFolder ?? Path.Combine(Path.GetTempPath(), "KickDownloader"),
            SubMessages = false,
            ChatBadges = true,
            Timestamp = s.Timestamp,
            Offline = true,
            EmojiVendor = TwitchDownloaderCore.Chat.EmojiVendor.GoogleNotoColor,
            EmoteScale = s.EmoteScale,
            DisperseCommentOffsets = false,
            AdjustUsernameVisibility = true,
        };
    }

    public static async Task RenderAsync(RenderSettings settings, ITaskProgress progress, CancellationToken ct = default)
    {
        var options = BuildOptions(settings);
        Directory.CreateDirectory(options.TempFolder);
        var outDir = Path.GetDirectoryName(Path.GetFullPath(settings.OutputFile));
        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
        }

        try
        {
            using var renderer = new ChatRenderer(options, progress);
            await renderer.ParseJsonAsync(ct).ConfigureAwait(false);
            await renderer.RenderVideoAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                File.Delete(settings.OutputFile); // half-written output of a failed or cancelled render
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

/// <summary>Console progress/log sink for the renderer. Logs go to stderr so stdout stays clean.</summary>
public sealed class ConsoleTaskProgress(bool verbose = false) : ITaskProgress
{
    private int _lastPercent = -1;

    public void LogVerbose(string logMessage) { if (verbose) Console.Error.WriteLine(logMessage); }
    public void LogVerbose(DefaultInterpolatedStringHandler logMessage) => LogVerbose(logMessage.ToStringAndClear());
    public void LogInfo(string logMessage) => Console.Error.WriteLine(logMessage);
    public void LogInfo(DefaultInterpolatedStringHandler logMessage) => LogInfo(logMessage.ToStringAndClear());
    public void LogWarning(string logMessage) => Console.Error.WriteLine($"warning: {logMessage}");
    public void LogWarning(DefaultInterpolatedStringHandler logMessage) => LogWarning(logMessage.ToStringAndClear());
    public void LogError(string logMessage) => Console.Error.WriteLine($"error: {logMessage}");
    public void LogError(DefaultInterpolatedStringHandler logMessage) => LogError(logMessage.ToStringAndClear());
    public void LogFfmpeg(string logMessage) { if (verbose) Console.Error.WriteLine($"ffmpeg: {logMessage}"); }
    public void SetStatus(string status) => Console.Error.WriteLine(status);
    public void SetTemplateStatus(string statusTemplate, int initialPercent) => SetStatus(string.Format(statusTemplate, initialPercent));
    public void SetTemplateStatus(string statusTemplate, int initialPercent, TimeSpan initialTime1, TimeSpan initialTime2) =>
        SetStatus(string.Format(statusTemplate, initialPercent, initialTime1, initialTime2));

    public void ReportProgress(int percent)
    {
        if (percent == _lastPercent)
        {
            return;
        }

        _lastPercent = percent;
        Console.Error.Write($"\rrender {percent}%   ");
    }

    public void ReportProgress(int percent, TimeSpan time1, TimeSpan time2) => ReportProgress(percent);
}
