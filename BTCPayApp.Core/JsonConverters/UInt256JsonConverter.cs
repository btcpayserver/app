using System.Text.Json;
using System.Text.Json.Serialization;
using BTCPayServer.Lightning;
using NBitcoin;

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

public static class NetworkHelper
{
    public static T Try<T>(Func<Network, T> func)
    {
        Exception? lastException = null;
        foreach (var network in Network.GetNetworks())
        {
            try
            {
                return func.Invoke(network);
            }
            catch (Exception e)
            {
                lastException = e;
            }
        }

        throw lastException!;
    }
}

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