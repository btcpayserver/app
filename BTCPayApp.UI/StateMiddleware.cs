using BTCPayApp.Core;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.UI.Features;
using Fluxor;

namespace BTCPayApp.UI;

public class StateMiddleware : Middleware
{
    private readonly IConfigProvider _configProvider;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly LightningNodeService _lightningNodeService;

    public const string UiStateConfigKey = "uistate";

    public StateMiddleware(
        IConfigProvider configProvider,
        BTCPayConnectionManager btcPayConnectionManager,
        LightningNodeService lightningNodeService)
    {
        _configProvider = configProvider;
        _btcPayConnectionManager = btcPayConnectionManager;
        _lightningNodeService = lightningNodeService;
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
        _btcPayConnectionManager.ConnectionChanged += (sender, args) =>
            dispatcher.Dispatch(new RootState.BTCPayConnectionUpdatedAction(_btcPayConnectionManager.Connection?.State));

        _lightningNodeService.OnStateChanged += (sender, args) =>
            dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(args));

        if (_lightningNodeService.State is not LightningNodeState.Running)
            dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.LightningState, true));

        
    }
}
