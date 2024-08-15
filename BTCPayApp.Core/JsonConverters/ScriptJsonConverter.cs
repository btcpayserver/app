using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin;

namespace BTCPayApp.Core.JsonConverters;

public class ScriptJsonConverter : JsonConverter<Script>
{
    public override Script? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string");
        }

        return new Script(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, Script value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}