using BTCPayServer.Lightning;
using LightningPayment = BTCPayApp.CommonServer.Models.LightningPayment;

namespace BTCPayApp.Core.LSP.JIT;

public interface IJITService
{
    public string ProviderName { get; }
    public Task<(LightMoney invoiceAmount, LightMoney fee)> CalculateInvoiceAmount(LightMoney expectedAmount);
    public Task WrapInvoice(LightningPayment lightningPayment);
}