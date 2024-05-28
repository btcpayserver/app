using BTCPayApp.Core.Attempt2;
using BTCPayServer.Lightning;

namespace BTCPayApp.Core.LSP.JIT;

public class BTCPayJIT: IJITService
{
    public BTCPayJIT(BTCPayConnectionManager btcPayConnectionManager)
    {
        
    }

    public string ProviderName => "BTCPayServer";

    public async Task<BOLT11PaymentRequest> WrapInvoice(BOLT11PaymentRequest invoice)
    {
        throw new NotImplementedException();
    }
}

public interface IJITService
{
    public string ProviderName { get; }
    public Task<BOLT11PaymentRequest> WrapInvoice(BOLT11PaymentRequest invoice);
}