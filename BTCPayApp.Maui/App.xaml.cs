using BTCPayApp.Core;

namespace BTCPayApp.Maui;

public partial class App : Application
{
    private readonly BTCPayAppConfigManager _btcPayAppConfigManager;
    private readonly BTCPayConnection _btcPayConnection;
    private readonly LightningNodeManager _lightningNodeManager;

    public App(
        BTCPayConnection btcPayConnection,
        BTCPayAppConfigManager btcPayAppConfigManager,
        LightningNodeManager lightningNodeManager)
    {
        _btcPayConnection = btcPayConnection;
        _btcPayAppConfigManager = btcPayAppConfigManager;
        _lightningNodeManager = lightningNodeManager;

        InitializeComponent();

        MainPage = new MainPage();
    }

    protected override Window CreateWindow(IActivationState activationState)
    {
        var window = base.CreateWindow(activationState);

        // IHostedService does not get started automatically on Android, so start them manually here.
        // Details: https://github.com/dotnet/maui/issues/2244#issuecomment-1470321259

        // https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/app-lifecycle

        // Raised after the native window has been created. At this point the cross-platform window
        // will have a native window handler, but the window might not be visible yet.
        window.Created += (s, e) =>
        {
#if ANDROID
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _btcPayConnection.StartAsync(default);
            _btcPayAppConfigManager.StartAsync(default);
            _lightningNodeManager.StartAsync(default);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#endif
        };

        // Raised when the window is no longer visible. There's no guarantee that an app will resume
        // from this state, because it may be terminated by the operating system.
        // -> Disconnect from any long running processes and cancel requests consuming resources.
        window.Stopped += (s, e) =>
        {
        };

        // Raised when an app resumes after being stopped. This event won't be raised the first time
        // the app launches, and can only be raised if the Stopped event has previously been raised.
        // -> Subscribe to any required events, and refresh any content that's on the visible page.
        window.Resumed += (s, e) =>
        {
        };

        // Raised when the native window is being destroyed and deallocated.
        // -> Remove any event subscriptions attached to the native window.
        window.Destroying += (s, e) =>
        {
#if ANDROID
            _btcPayConnection.StopAsync(default);
            _btcPayAppConfigManager.StopAsync(default);
            _lightningNodeManager.StopAsync(default);
#endif
        };

        return window;
    }
}
