using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Models;
using Plugin.NFC;

public class NfcService : INfcService, IDisposable
{
    public event EventHandler<NfcLnUrlRecord> OnNfcDataReceived = delegate { };

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

    private void Current_OnMessageReceived(ITagInfo tagInfo)
    {
        if (tagInfo == null || tagInfo.Records == null || !tagInfo.Records.Any())
        {
            throw new ArgumentException("No NFC records found in the tag info.");
        }

        var record = tagInfo.Records[0];

        var lnUrlRecord = new NfcLnUrlRecord
        {
            Payload = record.Payload,
            LnUrl = record.Message,
        };

        OnNfcDataReceived?.Invoke(this, lnUrlRecord);
    }

    public void Dispose()
    {
        CrossNFC.Current.OnMessageReceived -= Current_OnMessageReceived;
    }
}