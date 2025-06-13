using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Models;

namespace BTCPayApp.Server.Services
{
    public class NfcService : INfcService
    {
        public event EventHandler<NfcLnUrlRecord> OnNfcDataReceived = delegate { };

        public void Dispose()
        {
        }

        public void EndNfc()
        {
        }

        public void StartNfc()
        {
            // NFC for web is supported within the btcpayserver iframe, so we dont need to implement NFC support here.
        }
    }
}
