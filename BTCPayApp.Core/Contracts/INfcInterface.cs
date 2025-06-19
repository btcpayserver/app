using BTCPayApp.Core.Models;

namespace BTCPayApp.Core.Contracts;
public interface INfcService: IDisposable 
{
    event EventHandler<NfcCardData> OnNfcDataReceived;
    void StartNfc();
    void EndNfc();
}

public class NfcCardData
{
    public string Message { get; set; }
    public byte[] Payload { get; set; }
}
