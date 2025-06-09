using BTCPayApp.Core.BTCPayServer;
using Plugin.NFC;

public class NfcService : INfcService
{
    public event EventHandler<string> OnNfcDataReceived = delegate { };

    public void StartNfc()
    {
        if (!CrossNFC.IsSupported)
        {
            OnNfcDataReceived?.Invoke(this, "NFC not supported on this device");
            return;
        }

        if (!CrossNFC.Current.IsEnabled)
        {
            OnNfcDataReceived?.Invoke(this, "NFC is disabled. Please enable it.");
            return;
        }

        CrossNFC.Current.OnMessageReceived += Current_OnMessageReceived;
        CrossNFC.Current.StartListening();
    }

    private void Current_OnMessageReceived(ITagInfo tagInfo)
    {
        if (tagInfo == null || tagInfo.Records == null || !tagInfo.Records.Any())
        {
            OnNfcDataReceived?.Invoke(this, "No NDEF data found on the tag");
            return;
        }

        foreach (var record in tagInfo.Records)
        {
            string data = string.Empty;

            // Handle URI records
            if (record.TypeFormat == NFCNdefTypeFormat.Uri)
            {
                data = record.Message; // e.g., "lightning:lnbc..."
                data = $"URI: {data}";
            }
            // Handle Well-Known Text records
            else if (record.TypeFormat == NFCNdefTypeFormat.WellKnown && record.TypeFormat == NFCNdefTypeFormat.WellKnown && record.Payload != null)
            {
                // Decode text payload (Well-Known Text records have a specific format)
                data = DecodeTextRecord(record);
                data = $"Text: {data}";
            }
            else
            {
                // Handle other types (e.g., Unknown, Mime)
                data = $"Unsupported record type: {record.TypeFormat}";
            }

            // Check for Lightning-specific data
            if (data.Contains("lightning:") || data.Contains("lnbc"))
            {
                OnNfcDataReceived?.Invoke(this, data);
            }
            else
            {
                OnNfcDataReceived?.Invoke(this, $"Unsupported NFC data: {data}");
            }
        }
    }

    // Helper method to decode Well-Known Text records
    private string DecodeTextRecord(NFCNdefRecord record)
    {
        if (record.Payload == null || record.Payload.Length == 0)
            return string.Empty;

        try
        {
            // Well-Known Text record format: [Status Byte][Language Code][Text]
            byte statusByte = record.Payload[0];
            int languageCodeLength = statusByte & 0x3F; // Lower 6 bits indicate language code length
            int textStartIndex = 1 + languageCodeLength;

            if (textStartIndex >= record.Payload.Length)
                return string.Empty;

            // Extract text (UTF-8 encoded)
            string text = System.Text.Encoding.UTF8.GetString(
                record.Payload,
                textStartIndex,
                record.Payload.Length - textStartIndex);
            return text;
        }
        catch
        {
            return "Error decoding text record";
        }
    }

    public void Dispose()
    {
        CrossNFC.Current.OnMessageReceived -= Current_OnMessageReceived;
    }
}