using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Web.Services;

// Market candle timestamps are Taiwan wall-clock times in the desktop cache.
// Normalize offset-bearing legacy JSON before a browser in another timezone reads it.
public sealed class TaiwanDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        string value = reader.GetString() ?? throw new JsonException("Missing date/time");
        bool hasOffset = value.EndsWith('Z') ||
            (value.Length >= 6 && (value[^6] == '+' || value[^6] == '-'));
        return hasOffset
            ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture)
                .ToOffset(TimeSpan.FromHours(8)).DateTime
            : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Unspecified));
}
