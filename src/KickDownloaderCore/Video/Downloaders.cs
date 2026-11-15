using System.Globalization;
using KickDownloaderCore.Api;
using KickDownloaderCore.Models;

namespace KickDownloaderCore.Video;

internal static class MediaGuard
{
    /// <summary>ffmpeg follows whatever URL it is given, so only https hosts under kick.com are accepted.</summary>
    public static Uri RequireKickHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host == "kick.com" || uri.Host.EndsWith(".kick.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"Refusing media URL outside https://*.kick.com: {url}");
        }

        return uri;
    }

    public static void RequireFreeOutput(string path)
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
    }

    /// <summary>Removes the half-written output of a failed or cancelled run (the file did not exist before the run).</summary>
    public static void TryDeletePartial(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static IProgress<TimeSpan>? ToFraction(IProgress<double>? progress, TimeSpan total) =>
        progress is null || total <= TimeSpan.Zero
            ? null
            : new Progress<TimeSpan>(t => progress.Report(Math.Clamp(t / total, 0.0, 1.0)));

    public static readonly string[] Common =
        ["-hide_banner", "-loglevel", "error", "-protocol_whitelist", "https,tls,tcp,crypto,data"];
}

public sealed class ClipDownloader(KickApiClient api, IFfmpegRunner ffmpeg)
{
    public async Task<string> DownloadAsync(
        string clipId, string outputPath, CancellationToken ct = default, IProgress<double>? progress = null)
    {
        MediaGuard.RequireFreeOutput(outputPath);
        var clip = await api.GetClipAsync(clipId, ct).ConfigureAwait(false);
        var url = MediaGuard.RequireKickHttps(string.IsNullOrEmpty(clip.ClipUrl) ? clip.VideoUrl : clip.ClipUrl);
        try
        {
            await ffmpeg.RunAsync([.. MediaGuard.Common, "-i", url.AbsoluteUri, "-c", "copy", outputPath], ct,
                MediaGuard.ToFraction(progress, TimeSpan.FromSeconds(clip.DurationSeconds))).ConfigureAwait(false);
        }
        catch
        {
            MediaGuard.TryDeletePartial(outputPath);
            throw;
        }

        progress?.Report(1.0);
        return outputPath;
    }
}

/// <param name="Video">VOD details.</param>
/// <param name="Variants">Available qualities, as listed by the master playlist.</param>
/// <param name="ChannelSlug">Channel the VOD belongs to (null if Kick did not say).</param>
/// <param name="Duration">VOD length.</param>
public sealed record VodInfo(KickVideo Video, IReadOnlyList<HlsVariant> Variants, string? ChannelSlug, TimeSpan Duration);

public sealed class VodDownloader(KickApiClient api, IKickTransport transport, IFfmpegRunner ffmpeg)
{
    public async Task<VodInfo> GetInfoAsync(KickVodRef vod, CancellationToken ct = default)
    {
        var video = await api.GetVideoAsync(vod, ct).ConfigureAwait(false);
        var master = MediaGuard.RequireKickHttps(video.Source);
        var text = await transport.GetStringAsync(master, ct).ConfigureAwait(false);
        return new VodInfo(
            video,
            HlsMaster.Parse(text, master),
            video.Livestream?.Channel?.Slug,
            TimeSpan.FromMilliseconds(video.Livestream?.DurationMs ?? 0));
    }

    /// <summary>
    /// Downloads a VOD or a section of it. Stream copy, so the cut starts at the segment/keyframe at or before
    /// <paramref name="start"/> (typically a few seconds early).
    /// </summary>
    public async Task<string> DownloadAsync(
        KickVodRef vod, string outputPath, TimeSpan? start = null, TimeSpan? duration = null,
        string? quality = null, CancellationToken ct = default, IProgress<double>? progress = null)
    {
        if (start < TimeSpan.Zero || duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Start must be >= 0 and duration > 0.");
        }

        MediaGuard.RequireFreeOutput(outputPath);
        var info = await GetInfoAsync(vod, ct).ConfigureAwait(false);
        var variant = HlsMaster.Select(info.Variants, quality);
        MediaGuard.RequireKickHttps(variant.Url.AbsoluteUri);

        var args = new List<string>(MediaGuard.Common);
        if (start is { } s)
        {
            args.AddRange(["-ss", s.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)]);
        }

        args.AddRange(["-i", variant.Url.AbsoluteUri]);
        if (duration is { } d)
        {
            args.AddRange(["-t", d.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)]);
        }

        args.AddRange(["-c", "copy", outputPath]);
        var total = duration ?? (info.Duration - (start ?? TimeSpan.Zero));
        try
        {
            await ffmpeg.RunAsync(args, ct, MediaGuard.ToFraction(progress, total)).ConfigureAwait(false);
        }
        catch
        {
            MediaGuard.TryDeletePartial(outputPath);
            throw;
        }

        progress?.Report(1.0);
        return outputPath;
    }
}
