using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace KickDownloaderCore.Video;

public interface IFfmpegRunner
{
    /// <param name="progress">Receives the output position (ffmpeg's out_time) while it runs.</param>
    Task RunAsync(IReadOnlyList<string> args, CancellationToken ct = default, IProgress<TimeSpan>? progress = null);
}

/// <summary>Runs ffmpeg without a shell; arguments are passed as a list, never concatenated.</summary>
public sealed class FfmpegRunner(string ffmpegPath = "ffmpeg") : IFfmpegRunner
{
    private const string OutTimePrefix = "out_time_us=";

    public async Task RunAsync(IReadOnlyList<string> args, CancellationToken ct = default, IProgress<TimeSpan>? progress = null)
    {
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (progress is not null)
        {
            psi.ArgumentList.Add("-progress");
            psi.ArgumentList.Add("pipe:1");
            psi.ArgumentList.Add("-nostats");
        }

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };
        var err = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (err) { err.AppendLine(e.Data); }
            }
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (progress is not null && e.Data is { } line && line.StartsWith(OutTimePrefix, StringComparison.Ordinal)
                && long.TryParse(line.AsSpan(OutTimePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var micros))
            {
                progress.Report(TimeSpan.FromTicks(micros * 10));
            }
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new FileNotFoundException($"Could not start ffmpeg at '{ffmpegPath}'. Is it installed and on PATH?", ex);
        }

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); // release the output file handle
            throw;
        }

        if (process.ExitCode != 0)
        {
            string tail;
            lock (err) { tail = string.Join('\n', err.ToString().Split('\n').TakeLast(8)); }
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}:\n{tail}");
        }
    }
}
