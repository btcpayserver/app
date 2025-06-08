using BTCPayApp.Core.BTCPayServer;
using Plugin.NFC;

namespace BTCPayApp.Maui.Services;
public class NfcService : INfcService
{
    public event Action<string> TagDetected = delegate { }; // Initialize with an empty delegate to avoid nullability issues.
    private bool _isListening;

    public NfcService()
    {
        // Check if NFC is supported
        if (!CrossNFC.IsSupported)
        {
            TagDetected?.Invoke("NFC is not supported on this device.");
            return;
        }
    }

    public void StartListening()
    {
        if (_isListening || !CrossNFC.IsSupported)
            return;

        try
        {
            // Check if NFC is enabled
            if (CrossNFC.Current.IsEnabled)
            {
                _isListening = true;
                CrossNFC.Current.OnMessageReceived += OnMessageReceived;
                CrossNFC.Current.StartListening();
                TagDetected?.Invoke("Started listening for NFC tags...");
            }
            else
            {
                TagDetected?.Invoke("Please enable NFC in Settings.");
            }
        }
        catch (Exception ex)
        {
            TagDetected?.Invoke($"Error starting NFC: {ex.Message}");
        }
    }

    public void StopListening()
    {
        if (_isListening)
        {
            CrossNFC.Current.OnMessageReceived -= OnMessageReceived;
            CrossNFC.Current.StopListening();
            _isListening = false;
            TagDetected?.Invoke("Stopped listening for NFC tags.");
        }
    }

    private void OnMessageReceived(ITagInfo tagInfo)
    {
        if (tagInfo == null || tagInfo.IsEmpty)
        {
            TagDetected?.Invoke("No data found on NFC tag.");
            return;
        }

        // Extract tag ID
        string tagId = tagInfo.SerialNumber;

        // Process NDEF records
        string message = string.Empty;
        if (tagInfo.Records != null)
        {
            foreach (var record in tagInfo.Records)
            {
                message += $"Record: {record.Message}\n";
            }
        }

        TagDetected?.Invoke($"Tag ID: {tagId}\nData: {message}");
    }
}
