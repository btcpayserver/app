using System.Text.Json;
using System.Text.Json.Serialization;
using BTCPayServer.Lightning;

namespace BTCPayApp.Core.JsonConverters;

public class BOLT11PaymentRequestJsonConverter : JsonConverter<BOLT11PaymentRequest>
{
    public override BOLT11PaymentRequest? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string");
        }

        var str = reader.GetString();
        return NetworkHelper.Try(network => BOLT11PaymentRequest.Parse(str, network));
    }

    public override void Write(Utf8JsonWriter writer, BOLT11PaymentRequest value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}