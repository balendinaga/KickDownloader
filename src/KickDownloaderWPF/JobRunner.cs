using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using KickDownloaderCore.Api;
using TwitchDownloaderCore.Interfaces;

namespace KickDownloaderWPF;

/// <param name="OutputPath">File produced by the job, if any (enables "open folder").</param>
/// <param name="Message">Replaces the default "done" status text.</param>
public sealed record JobResult(string? OutputPath = null, string? Message = null);

/// <summary>
/// Runs one background job at a time and exposes its state for the status bar.
/// Create it on the UI thread: progress reports are marshalled back through that thread's context.
/// </summary>
public sealed class JobRunner : INotifyPropertyChanged
{
    private CancellationTokenSource? _cts;
    private bool _isBusy;
    private bool _isIndeterminate;
    private bool _hasError;
    private double _percent;
    private string _status = "Ready.";
    private string? _outputPath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); Raise(nameof(NotBusy)); Raise(nameof(CanOpenFolder)); } }
    public bool NotBusy => !IsBusy;
    public bool IsIndeterminate { get => _isIndeterminate; private set => Set(ref _isIndeterminate, value); }
    public bool HasError { get => _hasError; private set => Set(ref _hasError, value); }
    public double Percent { get => _percent; private set => Set(ref _percent, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string? OutputPath { get => _outputPath; private set { Set(ref _outputPath, value); Raise(nameof(CanOpenFolder)); } }
    public bool CanOpenFolder => !IsBusy && OutputPath is not null;

    public void Cancel()
    {
        if (_cts is { IsCancellationRequested: false } cts)
        {
            Status = "Cancelling…";
            cts.Cancel();
        }
    }

    public async Task RunAsync(string title, bool indeterminate, Func<IProgress<double>, CancellationToken, Task<JobResult>> work)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        HasError = false;
        OutputPath = null;
        Percent = 0;
        IsIndeterminate = indeterminate;
        Status = $"{title}…";
        using var cts = new CancellationTokenSource();
        _cts = cts;
        var progress = new Progress<double>(f =>
        {
            if (!IsBusy)
            {
                return; // a late report must not overwrite the final status
            }

            Percent = Math.Clamp(f, 0, 1) * 100;
            IsIndeterminate = false;
        });

        // 429 waits are retried inside the transport, so the job just continues where it stopped.
        var runningStatus = Status;
        var rateLimit = new Progress<RateLimitNotice>(n =>
        {
            if (IsBusy && !cts.IsCancellationRequested)
            {
                Status = n.SecondsLeft > 0
                    ? $"Rate limited. Waiting {n.SecondsLeft} s, then resuming… (retry {n.Attempt}/{n.MaxAttempts})"
                    : runningStatus;
            }
        });
        void OnRateLimited(RateLimitNotice n) => ((IProgress<RateLimitNotice>)rateLimit).Report(n);
        App.Transport.RateLimited += OnRateLimited;

        try
        {
            var result = await Task.Run(() => work(progress, cts.Token)).ConfigureAwait(true);
            OutputPath = result.OutputPath;
            Percent = 100;
            Status = result.Message ?? (result.OutputPath is null ? "Done." : $"Done: {Path.GetFileName(result.OutputPath)}");
        }
        catch (OperationCanceledException)
        {
            Percent = 0;
            Status = "Cancelled.";
        }
        catch (Exception ex)
        {
            HasError = true;
            Percent = 0;
            Status = ErrorText.Describe(ex);
        }
        finally
        {
            App.Transport.RateLimited -= OnRateLimited;
            _cts = null;
            IsIndeterminate = false;
            IsBusy = false;
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Raise(name);
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class ErrorText
{
    public static string Describe(Exception ex) => ex switch
    {
        KickVodNeedsChannelException =>
            "This VOD id uses the new format and cannot be looked up without the channel name. Paste the full link from your browser (kick.com/channel/videos/…).",
        KickBlockedException => "Kick blocked the request (403). This may be Cloudflare protection; try again in a while.",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "Not found (404). The link may be wrong, or the VOD/clip may have been deleted.",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "Kick says it received too many requests (429) and all retries were used up. Wait a few minutes and try again.",
        HttpRequestException h => $"Network error: {First(h.Message)}",
        FileNotFoundException f when f.Message.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) =>
            "FFmpeg not found. Set the path to ffmpeg.exe in the field at the top.",
        InvalidOperationException i when i.Message.StartsWith("ffmpeg", StringComparison.OrdinalIgnoreCase) => $"FFmpeg error: {First(i.Message)}",
        ArgumentException or IOException or InvalidDataException => First(ex.Message),
        _ => $"{ex.GetType().Name}: {First(ex.Message)}",
    };

    private static string First(string message) => message.Split('\n', 2)[0].Trim();
}

/// <summary>Feeds the vendored renderer's progress into a normal IProgress and collects its warnings.</summary>
public sealed class UiTaskProgress(IProgress<double> progress) : ITaskProgress
{
    public List<string> Warnings { get; } = [];

    public void LogVerbose(string logMessage) { }
    public void LogVerbose(DefaultInterpolatedStringHandler logMessage) => logMessage.ToStringAndClear();
    public void LogInfo(string logMessage) { }
    public void LogInfo(DefaultInterpolatedStringHandler logMessage) => logMessage.ToStringAndClear();
    public void LogWarning(string logMessage) => Warnings.Add(logMessage);
    public void LogWarning(DefaultInterpolatedStringHandler logMessage) => Warnings.Add(logMessage.ToStringAndClear());
    public void LogError(string logMessage) => Warnings.Add(logMessage);
    public void LogError(DefaultInterpolatedStringHandler logMessage) => Warnings.Add(logMessage.ToStringAndClear());
    public void LogFfmpeg(string logMessage) { }
    public void SetStatus(string status) { }
    public void SetTemplateStatus(string statusTemplate, int initialPercent) { }
    public void SetTemplateStatus(string statusTemplate, int initialPercent, TimeSpan initialTime1, TimeSpan initialTime2) { }
    public void ReportProgress(int percent) => progress.Report(percent / 100.0);
    public void ReportProgress(int percent, TimeSpan time1, TimeSpan time2) => ReportProgress(percent);
}
