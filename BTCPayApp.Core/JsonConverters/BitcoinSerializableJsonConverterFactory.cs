using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin;

namespace BTCPayApp.Core.JsonConverters;

public class BitcoinSerializableJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(IBitcoinSerializable).IsAssignableFrom(typeToConvert);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(BitcoinSerializableJsonConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter) Activator.CreateInstance(converterType)!;
    }
}