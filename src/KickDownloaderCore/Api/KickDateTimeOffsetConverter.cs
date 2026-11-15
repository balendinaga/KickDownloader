using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KickDownloaderCore.Api;

/// <summary>
/// Kick mixes ISO-8601 ("2026-11-14T22:41:47+00:00", "...Z") and zone-less "2026-11-14 22:41:47".
/// Zone-less values are UTC (matches the offset-bearing value of the same VOD).
/// </summary>
public sealed class KickDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
        {
            return value;
        }

        throw new JsonException($"Unrecognized Kick timestamp '{text}'.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
}
