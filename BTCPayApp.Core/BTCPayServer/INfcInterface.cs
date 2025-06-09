
namespace BTCPayApp.Core.BTCPayServer;
    public interface INfcService
{
    event EventHandler<string> OnNfcDataReceived;
    void StartNfc();
    void Dispose();
}
