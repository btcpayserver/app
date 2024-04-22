using BTCPayApp.Core;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.UI.Features;
using Fluxor;

namespace BTCPayApp.UI;

public class StateMiddleware : Middleware
{
    private readonly IConfigProvider _configProvider;
    private readonly BTCPayConnection _btcPayConnection;
    private readonly WalletService _walletService;

    public const string UiStateConfigKey = "uistate";

    public StateMiddleware(
        IConfigProvider configProvider,
        BTCPayConnection btcPayConnection,
        WalletService walletService)
    {
        _configProvider = configProvider;
        _btcPayConnection = btcPayConnection;
        _walletService = walletService;
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
        _btcPayConnection.ConnectionChanged += (sender, args) =>
            dispatcher.Dispatch(new RootState.BTCPayConnectionUpdatedAction(_btcPayConnection.Connection?.State));

        _walletService.OnStateChanged += (sender, args) =>
            dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(args));

        if (_walletService.State is not LightningNodeState.Running)
            dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.LightningState, true));

        
    }
}
