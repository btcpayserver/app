using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Data;
using BTCPayServer.Lightning;

namespace BTCPayApp.UI.Models;

public enum TransactionPaymentMethod
{
    Onchain,
    Lightning
}

public enum TransactionType
{
    Send,
    Receive
}

public class TransactionModel
{
    public string? Id { get; set; }
    public LightMoney? Value { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? Status { get; set; }
    public TransactionType Type { get; set; }
    public TransactionPaymentMethod PaymentMethod { get; set; }
    public AppLightningPayment? LightningPayment { get; set; }
    public TxResp? OnchainTransaction { get; set; }
}
