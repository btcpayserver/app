using System.Text.Json;
using System.Text.Json.Serialization;
using BTCPayServer.Lightning;

namespace BTCPayApp.Core.JsonConverters;

public class LightMoneyJsonConverter : JsonConverter<LightMoney>
{
    public override LightMoney? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => LightMoney.Parse(reader.GetString()),
            JsonTokenType.Null => null,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public override void Write(Utf8JsonWriter writer, LightMoney value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}