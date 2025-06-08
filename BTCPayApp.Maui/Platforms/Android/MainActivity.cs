using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Nfc;
using Plugin.NFC;

namespace BTCPayApp.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Initialize NFC Plugin  
        CrossNFC.Init(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        CrossNFC.OnResume();

        var nfcAdapter = NfcAdapter.GetDefaultAdapter(this);
        if (nfcAdapter != null)
        {
            var intent = new Intent(this, GetType()).AddFlags(ActivityFlags.SingleTop);
            var pendingIntent = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.Immutable);
            nfcAdapter.EnableForegroundDispatch(this, pendingIntent, null, null);
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        // Handle NFC tag discovery
        CrossNFC.OnNewIntent(intent);
    }
}
