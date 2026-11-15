using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using KickDownloaderWPF.Views;

namespace KickDownloaderWPF;

public partial class MainWindow : Window
{
    private readonly JobRunner _runner = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _runner;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _runner.Cancel();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_runner.OutputPath is not { } path || !File.Exists(path))
        {
            return;
        }

        // Select the finished file in Explorer. Arguments go in as a list, so odd file names cannot inject anything.
        var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        psi.ArgumentList.Add($"/select,{path}");
        Process.Start(psi);
    }

    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select ffmpeg.exe", Filter = "ffmpeg|ffmpeg.exe|Programs|*.exe" };
        if (dialog.ShowDialog(this) == true)
        {
            AppSettings.Current.FfmpegPath = dialog.FileName;
        }
    }

    private void ClipTab_TransferToChat(object? sender, ChatTransfer transfer)
    {
        ChatTab.Apply(transfer);
        Tabs.SelectedItem = ChatTabItem;
    }
}
