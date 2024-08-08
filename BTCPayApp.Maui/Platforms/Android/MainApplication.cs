using Android.App;
using Android.Runtime;

namespace BTCPayApp.Maui;

#if DEBUG
[Application(
    UsesCleartextTraffic = true,
    Debuggable = true
    )]
#else
[Application]
#endif
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    
    
}