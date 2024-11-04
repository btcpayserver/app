using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin;

namespace BTCPayApp.Core.JsonConverters;

public class BitcoinSerializableJsonConverter<T> : JsonConverter<T> where T : IBitcoinSerializable
{
    
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string");
        }

        var bytes = Convert.FromHexString(reader.GetString());
        var instance = Activator.CreateInstance<T>();
        return NetworkHelper.Try(network =>
        {
            instance.ReadWrite(bytes, network);
            return instance;
        });
    }


    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(Convert.ToHexString(value.ToBytes()).ToLowerInvariant());
    }
}