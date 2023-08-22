using BTCPayApp.Core;
using BTCPayApp.Core.Contracts;
using BTCPayApp.UI.Features;
using Fluxor;

namespace BTCPayApp.UI;

public class StateMiddleware : Middleware
{
    private readonly IConfigProvider _configProvider;
    private readonly BTCPayAppConfigManager _btcPayAppConfigManager;
    private readonly BTCPayConnection _btcPayConnection;
    private readonly LightningNodeManager _lightningNodeManager;

    public const string UiStateConfigKey = "uistate";

    public StateMiddleware(IConfigProvider configProvider,
        BTCPayAppConfigManager btcPayAppConfigManager,
        BTCPayConnection btcPayConnection,
        LightningNodeManager lightningNodeManager)
    {
        _configProvider = configProvider;
        _btcPayAppConfigManager = btcPayAppConfigManager;
        _btcPayConnection = btcPayConnection;
        _lightningNodeManager = lightningNodeManager;
    }

    public override async Task InitializeAsync(IDispatcher dispatcher, IStore store)
    {
        if (store.Features.TryGetValue(typeof(UIState).FullName, out var uiStateFeature))
        {
            dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.UiState, true));
            var existing = await _configProvider.Get<UIState>(UiStateConfigKey);
            if (existing is not null)
            {
                uiStateFeature.RestoreState(existing);
            }
            uiStateFeature.StateChanged += async (sender, args) =>
            {
                await _configProvider.Set(UiStateConfigKey, (UIState)uiStateFeature.GetState());
            };
            dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.UiState, false));
        }



        await base.InitializeAsync(dispatcher, store);

        ListenIn(dispatcher);
    }

    private void ListenIn(IDispatcher dispatcher)
    {
        _btcPayAppConfigManager.PairConfigUpdated += (_, config) =>
            dispatcher.Dispatch(new RootState.PairConfigLoadedAction(config));

        _btcPayAppConfigManager.WalletConfigUpdated += (_, config) =>
            dispatcher.Dispatch(new RootState.WalletConfigLoadedAction(config));

        _btcPayConnection.ConnectionChanged += (sender, args) =>
            dispatcher.Dispatch(new RootState.BTCPayConnectionUpdatedAction(_btcPayConnection.Connection?.State));

        _lightningNodeManager.StateChanged += (sender, args) =>
            dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(args));

        if (_btcPayAppConfigManager.PairConfig is null)
            dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.PairConfig, true));
        else
            dispatcher.Dispatch(new RootState.PairConfigLoadedAction(_btcPayAppConfigManager.PairConfig));

        if (_btcPayAppConfigManager.WalletConfig is null)
            dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.WalletConfig, true));
        else
            dispatcher.Dispatch(new RootState.WalletConfigLoadedAction(_btcPayAppConfigManager.WalletConfig));

        _ = _btcPayAppConfigManager.Loaded.Task.ContinueWith(_ =>
        {
            dispatcher.Dispatch(new RootState.WalletConfigLoadedAction(_btcPayAppConfigManager.WalletConfig));
            dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.WalletConfig, false));

            dispatcher.Dispatch(new RootState.PairConfigLoadedAction(_btcPayAppConfigManager.PairConfig));
            dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.PairConfig, false));
        });
    }
}
