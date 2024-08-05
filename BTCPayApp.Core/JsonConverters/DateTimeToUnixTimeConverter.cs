using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTCPayApp.Core.JsonConverters;

public class DateTimeToUnixTimeConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return default;
            case JsonTokenType.Number:
                return DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64());
            case JsonTokenType.String:
                return DateTimeOffset.FromUnixTimeSeconds(long.Parse(reader.GetString()));
        }

        throw new JsonException("Expected number or string with a unix timestamp value");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.ToUnixTimeSeconds());
    }
}