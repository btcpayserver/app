using BTCPayApp.Core;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.UI.Features;
using Fluxor;
using Microsoft.AspNetCore.SignalR.Client;

namespace BTCPayApp.UI;

public class StateMiddleware : Middleware
{
    private readonly IConfigProvider _configProvider;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly LightningNodeManager _lightningNodeService;
    private readonly OnChainWalletManager _onChainWalletManager;

    public const string UiStateConfigKey = "uistate";

    public StateMiddleware(
        IConfigProvider configProvider,
        BTCPayConnectionManager btcPayConnectionManager,
        LightningNodeManager lightningNodeService,
        OnChainWalletManager onChainWalletManager)
    {
        _configProvider = configProvider;
        _btcPayConnectionManager = btcPayConnectionManager;
        _lightningNodeService = lightningNodeService;
        _onChainWalletManager = onChainWalletManager;
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
        dispatcher.Dispatch(new RootState.BTCPayConnectionUpdatedAction(_btcPayConnectionManager.Connection?.State));
        dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(_lightningNodeService.State));
        dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(_onChainWalletManager.State));
        
        _btcPayConnectionManager.ConnectionChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.BTCPayConnectionUpdatedAction(args));
            dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.Connection,
                args == HubConnectionState.Connecting));

            return Task.CompletedTask;
        };

        _lightningNodeService.StateChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(args.New));
            if (args.New is LightningNodeState.Loading)
                dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.LightningState, true));
            if (args.Old is LightningNodeState.Loading)
                dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.LightningState, false));
            return Task.CompletedTask;
        };
        
        _onChainWalletManager.StateChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(args.New));
            if (args.New is OnChainWalletState.Loading)
                dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.WalletState, true));
            if (args.Old is OnChainWalletState.Loading)
                dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.WalletState, false));
            return Task.CompletedTask;
        };


        
    }
}
