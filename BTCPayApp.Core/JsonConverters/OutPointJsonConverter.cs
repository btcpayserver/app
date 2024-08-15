using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin;

namespace BTCPayApp.Core.JsonConverters;

public class OutPointJsonConverter : JsonConverter<OutPoint>
{
    public override OutPoint? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string");
        }

        return OutPoint.Parse(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, OutPoint value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}