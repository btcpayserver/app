using BTCPayApp.Core.Models;

namespace BTCPayApp.Core.Contracts;
    public interface INfcService: IDisposable 
{
    event EventHandler<NfcLnUrlRecord> OnNfcDataReceived;
    void StartNfc();
}
