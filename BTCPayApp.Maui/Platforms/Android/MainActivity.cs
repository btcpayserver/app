using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Plugin.NFC;

namespace BTCPayApp.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        CrossNFC.Init(this);
        base.OnCreate(savedInstanceState);

#if DEBUG
        // Enable WebView debugging
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
        {
            Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true); // Fully qualify the WebView class
        }
#endif

    }

    protected override void OnResume()
    {
        base.OnResume();
        CrossNFC.OnResume();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        CrossNFC.OnNewIntent(intent);
    }
}
