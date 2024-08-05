using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin;

namespace BTCPayApp.Core.JsonConverters;

public class UInt256JsonConverter : JsonConverter<uint256>
{
    public override uint256? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string");
        }

        return uint256.Parse(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, uint256 value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}