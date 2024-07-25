using System.Text.Json;
using System.Text.Json.Serialization;
using BTCPayApp.Core.JsonConverters;
using BTCPayServer.Lightning;
using NBitcoin;

namespace BTCPayApp.Core.Data;

public class AppLightningPayment : VersionedData<AppLightningPayment>
{
    [JsonConverter(typeof(UInt256JsonConverter))]
    public uint256 PaymentHash { get; set; }

    public string PaymentId { get; set; }
    public string? Preimage { get; set; }

    [JsonConverter(typeof(UInt256JsonConverter))]
    public uint256 Secret { get; set; }

    public bool Inbound { get; set; }

    [JsonConverter(typeof(DateTimeToUnixTimeConverter))]
    public DateTimeOffset Timestamp { get; set; }

    [JsonConverter(typeof(LightMoneyJsonConverter))]
    public LightMoney Value { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LightningPaymentStatus Status { get; set; }

    [JsonConverter(typeof(BOLT11PaymentRequestJsonConverter))]
    public BOLT11PaymentRequest PaymentRequest { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement> AdditionalData { get; set; } = new();
}