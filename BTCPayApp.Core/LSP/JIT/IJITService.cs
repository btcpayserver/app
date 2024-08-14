using System.Text.Json.Serialization;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.JsonConverters;
using BTCPayServer.Lightning;
using org.ldk.structs;

namespace BTCPayApp.Core.LSP.JIT;

public interface IJITService
{
    public string ProviderName { get; }
    public Task<JITFeeResponse?> CalculateInvoiceAmount(LightMoney expectedAmount, CancellationToken cancellationToken = default);
    public Task<bool> WrapInvoice(AppLightningPayment lightningPayment, JITFeeResponse? feeReponse, CancellationToken cancellationToken = default);

    public Task<bool> IsAcceptable(AppLightningPayment lightningPayment, Event.Event_PaymentClaimable paymentClaimable, CancellationToken cancellationToken = default);
    public bool Active { get; }
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