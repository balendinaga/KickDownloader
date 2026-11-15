using System.Windows;
using System.Windows.Controls;
using KickDownloaderCore.Api;
using KickDownloaderCore.Rendering;
using Microsoft.Win32;

namespace KickDownloaderWPF.Views;

public partial class RenderTab : UserControl
{
    private sealed record FormatItem(string Label, RenderFormat Format, string Hint);

    private static readonly FormatItem[] Formats =
    [
        new("WebM VP9 (transparent)", RenderFormat.WebmVp9, "Small file; works in OBS and browsers. Has an alpha channel."),
        new("MOV ProRes 4444 (transparent)", RenderFormat.MovProRes, "Highest quality, has an alpha channel, but large: about 6 MB/s (60 s ≈ 360 MB)."),
        new("MP4 H.264 (not transparent)", RenderFormat.Mp4, "No alpha channel; choose a solid background color (e.g. #111111)."),
    ];

    public RenderTab()
    {
        InitializeComponent();
        FormatBox.ItemsSource = Formats;
        FormatBox.SelectionChanged += (_, _) => FormatHint.Text = (FormatBox.SelectedItem as FormatItem)?.Hint ?? "";
        FormatBox.SelectedIndex = 0;
    }

    private JobRunner Runner => (JobRunner)DataContext;

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select chat file", Filter = "Chat json|*.json|All files|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            InputBox.Text = dialog.FileName;
        }
    }

    private async void Render_Click(object sender, RoutedEventArgs e)
    {
        FormError.Text = "";
        var input = InputBox.Text.Trim().Trim('"');
        if (!File.Exists(input))
        {
            FormError.Text = "Chat .json file not found. Use 'Choose file…' to select it.";
            return;
        }

        if (FormatBox.SelectedItem is not FormatItem format)
        {
            FormError.Text = "Select an output format.";
            return;
        }

        if (!Num.TryInt(WidthBox.Text, out var width) || !Num.TryInt(HeightBox.Text, out var height)
            || width is < 100 or > 8192 || height is < 100 or > 8192 || width % 2 != 0 || height % 2 != 0)
        {
            FormError.Text = "Width and height must be even numbers between 100 and 8192.";
            return;
        }

        if (!Num.TryInt(FpsBox.Text, out var fps) || fps is < 1 or > 120)
        {
            FormError.Text = "FPS must be a whole number between 1 and 120.";
            return;
        }

        if (!Num.TryDouble(FontBox.Text, out var font) || font < 4 || font > 200)
        {
            FormError.Text = "Font size must be a number between 4 and 200.";
            return;
        }

        if (!Num.TryDouble(EmoteBox.Text, out var emote) || emote <= 0 || emote > 10)
        {
            FormError.Text = "Emote scale must be greater than 0 and at most 10.";
            return;
        }

        var background = BackgroundBox.Text.Trim();
        if (!SkiaSharp.SKColor.TryParse(background, out _))
        {
            FormError.Text = "Background color must be in #RRGGBB or #AARRGGBB format.";
            return;
        }

        var folder = AppSettings.Current.OutputFolder;
        var timestamp = TimestampBox.IsChecked == true;
        await Runner.RunAsync("Rendering chat", false, async (progress, ct) =>
        {
            Directory.CreateDirectory(folder);
            var output = OutputNaming.UniquePath(
                folder, Path.GetFileNameWithoutExtension(input) + "_render", ChatRenderService.ExpectedExtension(format.Format));
            var ui = new UiTaskProgress(progress);
            await ChatRenderService.RenderAsync(new RenderSettings
            {
                InputFile = input,
                OutputFile = output,
                Format = format.Format,
                Width = width,
                Height = height,
                Fps = fps,
                FontSize = font,
                EmoteScale = emote,
                BackgroundColor = background,
                Timestamp = timestamp,
                FfmpegPath = AppSettings.Current.FfmpegPath,
            }, ui, ct).ConfigureAwait(false);

            return new JobResult(output, ui.Warnings.Count > 0
                ? $"Done: {Path.GetFileName(output)} — warning: {ui.Warnings[0]}"
                : null);
        });
    }
}
