using System.Globalization;

namespace KickDownloaderWPF;

internal static class Num
{
    /// <summary>Accepts "1.5" and "1,5" regardless of the Windows locale.</summary>
    public static bool TryDouble(string? text, out double value) =>
        double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    public static bool TryInt(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static string StampForFile(TimeSpan t) => $"{(int)t.TotalHours:00}{t.Minutes:00}{t.Seconds:00}";
}
