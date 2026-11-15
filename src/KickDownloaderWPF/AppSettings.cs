using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace KickDownloaderWPF;

/// <summary>User settings shared by all tabs. Stored in %AppData%\KickDownloader\settings.json.</summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KickDownloader", "settings.json");

    private string _ffmpegPath = "ffmpeg";
    private string _outputFolder = DefaultFolder();

    public static AppSettings Current { get; } = Load();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FfmpegPath
    {
        get => _ffmpegPath;
        set => Set(ref _ffmpegPath, string.IsNullOrWhiteSpace(value) ? "ffmpeg" : value.Trim());
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set => Set(ref _outputFolder, string.IsNullOrWhiteSpace(value) ? DefaultFolder() : value.Trim());
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Stored(FfmpegPath, OutputFolder)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save must never break the app.
        }
    }

    private static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "KickDownloader");

    private static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath)) is { } s)
            {
                settings.FfmpegPath = s.FfmpegPath ?? "ffmpeg";
                settings.OutputFolder = s.OutputFolder ?? DefaultFolder();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or unreadable settings: start from defaults.
        }

        return settings;
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed record Stored(string? FfmpegPath, string? OutputFolder);
}
