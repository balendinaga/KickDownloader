using System.Windows;
using System.Windows.Controls;
using KickDownloaderCore.Api;
using KickDownloaderCore.Chat;

namespace KickDownloaderWPF.Views;

public partial class ChatTab : UserControl
{
    public ChatTab() => InitializeComponent();

    private JobRunner Runner => (JobRunner)DataContext;

    /// <summary>Fills the form from a clip: its VOD and the range the clip covers.</summary>
    public void Apply(ChatTransfer transfer)
    {
        // A full link keeps the channel, which UUIDv7 VOD ids need to be looked up.
        InputBox.Text = transfer.ChannelSlug is { Length: > 0 } slug
            ? $"https://kick.com/{slug}/videos/{transfer.VodUuid}"
            : transfer.VodUuid;
        TrimBox.IsChecked = true;
        StartBox.Text = TimeInput.Format(TimeSpan.FromSeconds(transfer.StartSeconds));
        EndBox.Text = TimeInput.Format(TimeSpan.FromSeconds(transfer.StartSeconds + transfer.DurationSeconds));
        FormError.Text = "";
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        FormError.Text = "";
        if (!KickInput.TryParseVodRef(InputBox.Text, out var vod))
        {
            FormError.Text = "Enter a valid Kick VOD link or UUID.";
            return;
        }

        var start = TimeSpan.Zero;
        TimeSpan? duration = null;
        if (TrimBox.IsChecked == true)
        {
            if (!TimeInput.TryParse(StartBox.Text, out start) || !TimeInput.TryParse(EndBox.Text, out var end))
            {
                FormError.Text = "Time must be hh:mm:ss or seconds (e.g. 01:30:00 or 5400).";
                return;
            }

            if (end <= start)
            {
                FormError.Text = "End time must be after the start time.";
                return;
            }

            duration = end - start;
        }

        var folder = AppSettings.Current.OutputFolder;
        var from = start;
        await Runner.RunAsync("Downloading chat", false, async (progress, ct) =>
        {
            Directory.CreateDirectory(folder);
            var video = await App.Api.GetVideoAsync(vod, ct).ConfigureAwait(false);
            var total = TimeSpan.FromMilliseconds(video.Livestream?.DurationMs ?? 0);
            if (from >= total)
            {
                throw new ArgumentException($"Start cannot be beyond the VOD length ({TimeInput.Format(total)}).");
            }

            if (duration is { } d && from + d > total)
            {
                throw new ArgumentException($"End cannot be beyond the VOD length ({TimeInput.Format(total)}).");
            }

            var truncated = 0;
            using var http = App.NewAssetClient();
            var downloader = new KickChatDownloader(App.Api, new HttpAssetFetcher(http))
            {
                OnPossibleTruncation = _ => Interlocked.Increment(ref truncated),
            };
            var root = await downloader.DownloadAsync(vod.Uuid, vod.ChannelSlug,
                new ChatDownloadOptions { Start = from, Duration = duration }, progress, ct).ConfigureAwait(false);

            var slug = video.Livestream?.Channel?.Slug ?? "kick";
            var range = duration is { } dd ? $"_{Num.StampForFile(from)}-{Num.StampForFile(from + dd)}" : "";
            var path = OutputNaming.UniquePath(folder, $"{slug}_{vod.Uuid[..8]}{range}_chat", ".json");
            await KickChatDownloader.WriteAsync(root, path, ct).ConfigureAwait(false);

            var note = $"{root.comments.Count} messages, {root.embeddedData.firstParty.Count} emotes";
            if (truncated > 0)
            {
                note += $" — {truncated} windows may be missing messages";
            }

            return new JobResult(path, $"Done: {Path.GetFileName(path)} ({note})");
        });
    }
}
