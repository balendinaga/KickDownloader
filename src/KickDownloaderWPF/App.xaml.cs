using System.Windows;
using KickDownloaderCore.Api;
using KickDownloaderCore.Video;

namespace KickDownloaderWPF;

public partial class App : Application
{
    /// <summary>One transport (and so one request throttle) for the whole app.</summary>
    public static HttpClientTransport Transport { get; } = HttpClientTransport.CreateDefault();

    public static KickApiClient Api { get; } = new(Transport);

    /// <summary>Built per job so a changed ffmpeg path is picked up without a restart.</summary>
    public static FfmpegRunner NewFfmpeg() => new(AppSettings.Current.FfmpegPath);

    public static VodDownloader NewVodDownloader() => new(Api, Transport, NewFfmpeg());

    public static HttpClient NewAssetClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        return http;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppSettings.Current.Save();
        base.OnExit(e);
    }
}
