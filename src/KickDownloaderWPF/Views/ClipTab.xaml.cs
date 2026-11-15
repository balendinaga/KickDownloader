using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KickDownloaderCore.Api;
using KickDownloaderCore.Models;
using KickDownloaderCore.Video;

namespace KickDownloaderWPF.Views;

/// <summary>The part of the VOD a clip was cut from, i.e. what the chat download needs.</summary>
public sealed record ChatTransfer(string VodUuid, int StartSeconds, int DurationSeconds, string? ChannelSlug = null);

public partial class ClipTab : UserControl
{
    private KickClip? _clip;

    public ClipTab() => InitializeComponent();

    public event EventHandler<ChatTransfer>? TransferToChat;

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
        if (!KickInput.TryParseClip(InputBox.Text, out var id))
        {
            FormError.Text = "Enter a valid Kick clip link or id (clip_…).";
            return;
        }

        KickClip? clip = null;
        await Runner.RunAsync("Fetching clip info", true, async (_, ct) =>
        {
            clip = await App.Api.GetClipAsync(id, ct).ConfigureAwait(false);
            return new JobResult(Message: "Clip info loaded.");
        });

        if (clip is not null)
        {
            Show(clip);
        }
    }

    private void Show(KickClip clip)
    {
        _clip = clip;
        InfoText.Text = $"{clip.Title}\n{clip.Channel?.Slug ?? "?"} · {clip.DurationSeconds} s";
        InfoCard.Visibility = Visibility.Visible;
        var canTransfer = clip.Vod is not null && clip.VodStartsAtSeconds is not null && clip.Channel is not null;
        TransferButton.IsEnabled = canTransfer;
        TransferHint.Text = canTransfer
            ? $"VOD {clip.Vod!.Id} · start {TimeInput.Format(TimeSpan.FromSeconds(clip.VodStartsAtSeconds!.Value))} · length {clip.DurationSeconds} s"
            : "This clip has no linked VOD, so its chat cannot be transferred.";
    }

    private void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is { Vod: { } vod, VodStartsAtSeconds: { } start })
        {
            TransferToChat?.Invoke(this, new ChatTransfer(vod.Id, start, _clip.DurationSeconds, _clip.Channel?.Slug));
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        FormError.Text = "";
        if (!KickInput.TryParseClip(InputBox.Text, out var id))
        {
            FormError.Text = "Enter a valid Kick clip link or id (clip_…).";
            return;
        }

        var folder = AppSettings.Current.OutputFolder;
        KickClip? clip = null;
        await Runner.RunAsync("Downloading clip", false, async (progress, ct) =>
        {
            Directory.CreateDirectory(folder);
            clip = await App.Api.GetClipAsync(id, ct).ConfigureAwait(false);
            var path = OutputNaming.UniquePath(folder, $"{clip.Channel?.Slug ?? "kick"}_{id[5..13]}", ".mp4");
            await new ClipDownloader(App.Api, App.NewFfmpeg()).DownloadAsync(id, path, ct, progress).ConfigureAwait(false);
            return new JobResult(path);
        });

        if (clip is not null)
        {
            Show(clip); // makes the "transfer to chat" button available right after the download
        }
    }
}
