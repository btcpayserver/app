using System.Text.Json;
using System.Text.Json.Serialization;
using BTCPayServer.Lightning;
using LightningPayment = BTCPayApp.CommonServer.Models.LightningPayment;

namespace BTCPayApp.Core.LSP.JIT;

public interface IJITService
{
    public string ProviderName { get; }
    public Task<JITFeeResponse?> CalculateInvoiceAmount(LightMoney expectedAmount);
    public Task<bool> WrapInvoice(LightningPayment lightningPayment, JITFeeResponse? feeReponse);
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

public record JITFeeResponse
{
    public JITFeeResponse(LightMoney AmountToRequestPayer, LightMoney AmountToGenerateOurInvoice, LightMoney LSPFee,
        string FeeIdentifier, string LSP)
    {
        this.AmountToRequestPayer = AmountToRequestPayer;
        this.AmountToGenerateOurInvoice = AmountToGenerateOurInvoice;
        this.LSPFee = LSPFee;
        this.FeeIdentifier = FeeIdentifier;
        this.LSP = LSP;
    }

    [JsonConverter(typeof(LightMoneyJsonConverter))]
    public LightMoney AmountToRequestPayer { get; init; }

    [JsonConverter(typeof(LightMoneyJsonConverter))]
    public LightMoney AmountToGenerateOurInvoice { get; init; }

    [JsonConverter(typeof(LightMoneyJsonConverter))]
    public LightMoney LSPFee { get; init; }

    public string FeeIdentifier { get; init; }

    public string LSP { get; set; }

    public void Deconstruct(out LightMoney AmountToRequestPayer, out LightMoney AmountToGenerateOurInvoice,
        out LightMoney LSPFee, out string FeeIdentifier)
    {
        AmountToRequestPayer = this.AmountToRequestPayer;
        AmountToGenerateOurInvoice = this.AmountToGenerateOurInvoice;
        LSPFee = this.LSPFee;
        FeeIdentifier = this.FeeIdentifier;
    }
}