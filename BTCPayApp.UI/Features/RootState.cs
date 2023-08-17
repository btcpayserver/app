using BTCPayApp.Core;
using BTCPayApp.UI.Pages;
using Fluxor;
using Fluxor.Blazor.Web.Middlewares.Routing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record RootState(bool Loading, BTCPayPairConfig? PairConfig, WalletConfig? WalletConfig,
    HubConnectionState? BTCPayServerConnectionState, LightningNodeState LightningNodeState)
{
    public RootState() : this(true, null, null, null, LightningNodeState.NotConfigured)
    {
    }

    public record PairConfigLoadedAction(BTCPayPairConfig? Config);

    public record WalletConfigLoadedAction(WalletConfig? Config);


    public record LoadingAction(bool Loading);

    protected class LoadingReducer : Reducer<RootState, LoadingAction>
    {
        public override RootState Reduce(RootState state, LoadingAction action)
        {
            return state with {Loading = action.Loading};
        }
    }

    protected class PairConfigLoadedReducer : Reducer<RootState, PairConfigLoadedAction>
    {
        public override RootState Reduce(RootState state, PairConfigLoadedAction action)
        {
            return state with {Loading = false, PairConfig = action.Config};
        }
    }

    protected class WalletConfigLoadedReducer : Reducer<RootState, WalletConfigLoadedAction>
    {
        public override RootState Reduce(RootState state, WalletConfigLoadedAction action)
        {
            return state with {Loading = false, WalletConfig = action.Config};
        }
    }

    protected class BTCPayConnectionUpdatedReducer : Reducer<RootState, BTCPayConnectionUpdatedAction>
    {
        public override RootState Reduce(RootState state, BTCPayConnectionUpdatedAction action)
        {
            return state with {Loading = false, BTCPayServerConnectionState = action.ConnectionState};
        }
    }

    protected class LightningNodeStateUpdatedReducer : Reducer<RootState, LightningNodeStateUpdatedAction>
    {
        public override RootState Reduce(RootState state, LightningNodeStateUpdatedAction action)
        {
            return state with {Loading = false, LightningNodeState = action.State};
        }
    }

    protected class PairConfigLoadedActionEffect : Effect<PairConfigLoadedAction>
    {
        private readonly NavigationManager _navigationManager;
        private readonly BTCPayAppConfigManager _btcPayAppConfigManager;
        private readonly IState<RootState> _state;

        public PairConfigLoadedActionEffect(NavigationManager navigationManager,
            BTCPayAppConfigManager btcPayAppConfigManager, IState<RootState> state)
        {
            _navigationManager = navigationManager;
            _btcPayAppConfigManager = btcPayAppConfigManager;
            _state = state;
        }

        public override async Task HandleAsync(PairConfigLoadedAction action, IDispatcher dispatcher)
        {
            switch (action.Config?.PairingResult)
            {
                //if no wallet and no pair configured, go to the start page
                case null when _state.Value.WalletConfig is null && !_navigationManager.Uri.EndsWith(Routes.Pair):
                    dispatcher.Dispatch(new GoAction(Routes.FirstRun));
                    break;
                //if wallet is configured and no pair, go to the pair page
                case null when _state.Value.WalletConfig?.StandaloneMode is not true &&
                               !_navigationManager.Uri.EndsWith(Routes.Pair):
                    dispatcher.Dispatch(new GoAction(Routes.Pair));
                    break;
                //if wallet is not configured and pair is configured, go to the wallet page
                case not null when _state.Value.WalletConfig is null &&
                                   !_navigationManager.Uri.EndsWith(Routes.WalletSetup):
                    dispatcher.Dispatch(new GoAction(Routes.WalletSetup));
                    break;
            }

            await _btcPayAppConfigManager.UpdateConfig(action.Config);
        }
    }

    protected class WalletConfigLoadedActionEffect : Effect<WalletConfigLoadedAction>
    {
        private readonly NavigationManager _navigationManager;
        private readonly BTCPayAppConfigManager _btcPayAppConfigManager;
        private readonly IState<RootState> _state;

        public WalletConfigLoadedActionEffect(NavigationManager navigationManager,
            BTCPayAppConfigManager btcPayAppConfigManager, IState<RootState> state)
        {
            _navigationManager = navigationManager;
            _btcPayAppConfigManager = btcPayAppConfigManager;
            _state = state;
        }

        public override async Task HandleAsync(WalletConfigLoadedAction action, IDispatcher dispatcher)
        {
            if (action.Config is null && _state.Value.PairConfig is not null &&
                !_navigationManager.Uri.EndsWith(Routes.WalletSetup))
                dispatcher.Dispatch(new GoAction(Routes.FirstRun));
            
            if (action.Config is not null && _state.Value.PairConfig is not null &&
                _navigationManager.Uri.EndsWith(Routes.WalletSetup))
                dispatcher.Dispatch(new GoAction(Routes.Splash));

            await _btcPayAppConfigManager.UpdateConfig(action.Config);
        }
    }

    public record BTCPayConnectionUpdatedAction(HubConnectionState? ConnectionState);
    public record LightningNodeStateUpdatedAction(LightningNodeState State);
}
