using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Contracts;
using BTCPayApp.UI.Features;
using Fluxor;

namespace BTCPayApp.UI;

public class StateMiddleware : Middleware
{
    private readonly IConfigProvider _configProvider;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly LightningNodeManager _lightningNodeService;
    private readonly OnChainWalletManager _onChainWalletManager;
    private readonly BTCPayAppServerClient _btcpayAppServerClient;

    public const string UiStateConfigKey = "uistate";

    public StateMiddleware(
        IConfigProvider configProvider,
        BTCPayConnectionManager btcPayConnectionManager,
        LightningNodeManager lightningNodeService,
        OnChainWalletManager onChainWalletManager,
        BTCPayAppServerClient btcpayAppServerClient)
    {
        _configProvider = configProvider;
        _btcPayConnectionManager = btcPayConnectionManager;
        _lightningNodeService = lightningNodeService;
        _onChainWalletManager = onChainWalletManager;
        _btcpayAppServerClient = btcpayAppServerClient;
    }

    public override async Task InitializeAsync(IDispatcher dispatcher, IStore store)
    {
        if (store.Features.TryGetValue(typeof(UIState).FullName, out var uiStateFeature))
        {
            var existing = await _configProvider.Get<UIState>(UiStateConfigKey);
            if (existing is not null)
            {
                uiStateFeature.RestoreState(existing);
            }
            uiStateFeature.StateChanged += async (sender, args) =>
            {
                var state = (UIState)uiStateFeature.GetState() with { Instance = null };
                await _configProvider.Set(UiStateConfigKey, state);
            };

            store.Initialized.ContinueWith(task => ListenIn(dispatcher));
        }

        await base.InitializeAsync(dispatcher, store);

    }

    private void ListenIn(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(_btcPayConnectionManager.ConnectionState));
        dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(_lightningNodeService.State));
        dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(_onChainWalletManager.State));

        _btcPayConnectionManager.ConnectionChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(_btcPayConnectionManager.ConnectionState));
            return Task.CompletedTask;
        };

        _lightningNodeService.StateChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(_lightningNodeService.State));
            return Task.CompletedTask;
        };

        _onChainWalletManager.StateChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(_onChainWalletManager.State));
            return Task.CompletedTask;
        };
        _btcpayAppServerClient.OnNotifyServerEvent += (sender, eventName) =>
        {
            if(eventName == "notifications-updated")
                dispatcher.Dispatch(new NotificationState.FetchNotifications());
            return Task.CompletedTask;
        };
    }
}
