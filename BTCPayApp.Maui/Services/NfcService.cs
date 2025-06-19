using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Models;
using Plugin.NFC;

public class NfcService : INfcService, IDisposable
{
    public event EventHandler<NfcCardData> OnNfcDataReceived = delegate { };

    public void StartNfc()
    {
        if (!CrossNFC.IsSupported)
        {
            return;
        }

        if (!CrossNFC.Current.IsEnabled)
        {
            return;
        }

        CrossNFC.Current.OnMessageReceived += Current_OnMessageReceived;
        CrossNFC.Current.StartListening();
    }
    public void EndNfc()
    {
        CrossNFC.Current.StopListening();
        Dispose();
    }

    private void Current_OnMessageReceived(ITagInfo tagInfo)
    {
        if (tagInfo == null || tagInfo.Records == null || !tagInfo.Records.Any())
        {
            //throw new ArgumentException("No NFC records found in the tag info.");
            return;
        }
        
        var record = tagInfo.Records[0];

        // Pass the raw tag info up - let the consumer decide what to do with it
        OnNfcDataReceived?.Invoke(this, new NfcCardData
        {
            Message = record.Message,
            Payload = record.Payload
        });
    }

    public void Dispose()
    {
        CrossNFC.Current.OnMessageReceived -= Current_OnMessageReceived;
    }
}