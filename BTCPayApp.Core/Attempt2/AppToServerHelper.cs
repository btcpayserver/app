using BTCPayApp.Core.Data;
using BTCPayServer.Lightning;

namespace BTCPayApp.Core.Attempt2;

public static class AppToServerHelper
{
    
    public static LightningInvoice ToInvoice(this AppLightningPayment lightningPayment)
    {
        return new LightningInvoice()
        {
            Id = lightningPayment.PaymentHash.ToString(),
            Amount = lightningPayment.Value,
            PaymentHash = lightningPayment.PaymentHash.ToString(),
            Preimage = lightningPayment.Preimage,
            ExpiresAt = lightningPayment.AdditionalData[PaymentsManager.LightningPaymentExpiryKey].GetDateTimeOffset(),
            PaidAt = lightningPayment.Status == LightningPaymentStatus.Complete? DateTimeOffset.UtcNow: null, //TODO: store these in ln payment
            BOLT11 = lightningPayment.PaymentRequest.ToString(),
            Status = lightningPayment.Status == LightningPaymentStatus.Complete? LightningInvoiceStatus.Paid: lightningPayment.PaymentRequest.ExpiryDate < DateTimeOffset.UtcNow? LightningInvoiceStatus.Expired: LightningInvoiceStatus.Unpaid,
            AmountReceived = lightningPayment.Status == LightningPaymentStatus.Complete? lightningPayment.Value: null
        };
    }

    public static LightningPayment ToPayment(this AppLightningPayment lightningPayment)
    {
        return new LightningPayment()
        {
            Id = lightningPayment.PaymentHash.ToString(),
            Amount = LightMoney.MilliSatoshis(lightningPayment.Value),
            PaymentHash = lightningPayment.PaymentHash.ToString(),
            Preimage = lightningPayment.Preimage,
            BOLT11 = lightningPayment.PaymentRequest.ToString(),
            Status = lightningPayment.Status,
            Fee = lightningPayment.AdditionalData.TryGetValue("feePaid", out var feePaid) ? LightMoney.MilliSatoshis((long)feePaid.GetInt64()) : null,
            CreatedAt = lightningPayment.Timestamp
            
        };
    }
    
    public static async Task<List<LightningPayment>> ToPayments(this Task<List<AppLightningPayment>> appLightningPayments)
    {
        var result = await appLightningPayments;
        return result.Select(ToPayment).ToList();
    }
    public static async Task<List<LightningInvoice>> ToInvoices(this Task<List<AppLightningPayment>> appLightningPayments)
    {
        var result = await appLightningPayments;
        return result.Select(ToInvoice).ToList();
    }
}