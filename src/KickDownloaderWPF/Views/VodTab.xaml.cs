using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KickDownloaderCore.Api;
using KickDownloaderCore.Video;

namespace KickDownloaderWPF.Views;

public partial class VodTab : UserControl
{
    private sealed record QualityItem(string Label, string? Quality);

    public VodTab()
    {
        InitializeComponent();
        QualityBox.ItemsSource = new[] { new QualityItem("Highest quality", null) };
        QualityBox.SelectedIndex = 0;
    }

    private JobRunner Runner => (JobRunner)DataContext;

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Info_Click(sender, e);
        }
    }

    private async void Info_Click(object sender, RoutedEventArgs e)
    {
        FormError.Text = "";
        if (!KickInput.TryParseVodRef(InputBox.Text, out var vod))
        {
            FormError.Text = "Enter a valid Kick VOD link or UUID.";
            return;
        }

        VodInfo? info = null;
        await Runner.RunAsync("Fetching VOD info", true, async (_, ct) =>
        {
            info = await App.NewVodDownloader().GetInfoAsync(vod, ct).ConfigureAwait(false);
            return new JobResult(Message: "VOD info loaded.");
        });

        if (info is not null)
        {
            Show(info);
        }
    }

    private void Show(VodInfo info)
    {
        var live = info.Video.Livestream;
        InfoText.Text = $"{live?.SessionTitle}\n{info.ChannelSlug ?? "?"} · length {TimeInput.Format(info.Duration)} · {live?.StartTime.LocalDateTime:g}";
        InfoCard.Visibility = Visibility.Visible;
        QualityBox.ItemsSource = new[] { new QualityItem("Highest quality", null) }
            .Concat(info.Variants.Select(v => new QualityItem(v.Resolution is null ? v.Name : $"{v.Name} ({v.Resolution})", v.Name)))
            .ToList();
        QualityBox.SelectedIndex = 0;
        if (TrimBox.IsChecked != true)
        {
            StartBox.Text = "00:00:00";
            EndBox.Text = TimeInput.Format(info.Duration);
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        FormError.Text = "";
        if (!KickInput.TryParseVodRef(InputBox.Text, out var vod))
        {
            FormError.Text = "Enter a valid Kick VOD link or UUID.";
            return;
        }

        var trim = TrimBox.IsChecked == true;
        TimeSpan? start = null, duration = null;
        if (trim)
        {
            if (!TimeInput.TryParse(StartBox.Text, out var s) || !TimeInput.TryParse(EndBox.Text, out var end))
            {
                FormError.Text = "Time must be hh:mm:ss or seconds (e.g. 01:30:00 or 5400).";
                return;
            }

            if (end <= s)
            {
                FormError.Text = "End time must be after the start time.";
                return;
            }

            start = s;
            duration = end - s;
        }

        var quality = (QualityBox.SelectedItem as QualityItem)?.Quality;
        var folder = AppSettings.Current.OutputFolder;
        await Runner.RunAsync("Downloading VOD", false, async (progress, ct) =>
        {
            Directory.CreateDirectory(folder);
            var downloader = App.NewVodDownloader();
            var info = await downloader.GetInfoAsync(vod, ct).ConfigureAwait(false);
            if (start is { } s0 && s0 >= info.Duration)
            {
                throw new ArgumentException($"Start cannot be beyond the VOD length ({TimeInput.Format(info.Duration)}).");
            }

            if (start is { } s1 && duration is { } d1 && s1 + d1 > info.Duration)
            {
                throw new ArgumentException($"End cannot be beyond the VOD length ({TimeInput.Format(info.Duration)}).");
            }

            var name = $"{info.ChannelSlug ?? "kick"}_{vod.Uuid[..8]}"
                       + (start is { } a && duration is { } d ? $"_{Num.StampForFile(a)}-{Num.StampForFile(a + d)}" : "");
            var path = OutputNaming.UniquePath(folder, name, ".mp4");
            await downloader.DownloadAsync(vod, path, start, duration, quality, ct, progress).ConfigureAwait(false);
            return new JobResult(path);
        });
    }
}
