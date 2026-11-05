using System.Text.Json;
using System.Text.Json.Serialization;

namespace Renewtron.Settings;

/// <summary>
/// Writes every DateTime the API returns as UTC ("…Z"). Renewtron stores times in UTC, but SQL
/// Server hands them back with no kind, and without the "Z" a browser reads them as its own local
/// time: in Sydney every "2 hours ago" came out about 11 hours wrong. Local times are converted;
/// reading is unchanged.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        });
}
