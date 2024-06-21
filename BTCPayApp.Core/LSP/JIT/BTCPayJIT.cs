using BTCPayApp.Core.Attempt2;
using BTCPayServer.Lightning;

namespace BTCPayApp.Core.LSP.JIT;

using System.Diagnostics;
using BTCPayServer.Lightning;

public class BTCPayJIT : IJITService
{
    public BTCPayJIT(BTCPayConnectionManager btcPayConnectionManager)
    {
    }

    public string ProviderName => "BTCPayServer";

    public async Task WrapInvoice(LightningPayment lightningPayment)
    {
        throw new NotImplementedException();
    }
}