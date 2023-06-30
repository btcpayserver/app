using BTCPayApp.Core;
using BTCPayApp.UI.Pages;
using Fluxor;
using Fluxor.Blazor.Web.Middlewares.Routing;
using Microsoft.AspNetCore.Components;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record RootState(bool Loading, BTCPayPairConfig? PairConfig, WalletConfig? WalletConfig)
{
    public RootState() : this(true, null, null)
    {
    }

    public record PairConfigLoadedAction(BTCPayPairConfig? Config);
    public record WalletConfigLoadedAction(WalletConfig? Config);


    public record LoadingAction(bool Loading);

    protected class PairConfigLoadedReducer : Reducer<RootState, PairConfigLoadedAction>
    {
        public override RootState Reduce(RootState state, PairConfigLoadedAction action)
        {
            return new RootState(false, action.Config, state.WalletConfig);
        }
    }
    protected class WalletConfigLoadedReducer : Reducer<RootState, WalletConfigLoadedAction>
    {
        public override RootState Reduce(RootState state, WalletConfigLoadedAction action)
        {
            return new RootState(false, state.PairConfig, action.Config);
        }
    }

    protected class PairConfigLoadedActionEffect : Effect<PairConfigLoadedAction>
    {
        private readonly NavigationManager _navigationManager;
        private readonly BTCPayAppConfigManager _BTCPayAppConfigManager;
        private readonly IState<RootState> _state;

        public PairConfigLoadedActionEffect(NavigationManager navigationManager,
            BTCPayAppConfigManager BTCPayAppConfigManager, IState<RootState>state)
        {
            _navigationManager = navigationManager;
            _BTCPayAppConfigManager = BTCPayAppConfigManager;
            _state = state;
        }

        public override async Task HandleAsync(PairConfigLoadedAction action, IDispatcher dispatcher)
        {
            switch (action.Config?.PairingResult)
            {
                case null when _state.Value.WalletConfig is null && !_navigationManager.Uri.EndsWith(Routes.Pair):
                    dispatcher.Dispatch(new GoAction(Routes.FirstRun));
                    break;
                case null when _state.Value.WalletConfig is not null &&
                               !_navigationManager.Uri.EndsWith(Routes.Pair):
                    dispatcher.Dispatch(new GoAction(Routes.Pair));
                    break;
                default:
                {
                    if (action.Config?.PairingResult is not null && _state.Value.WalletConfig is null && !_navigationManager.Uri.EndsWith(Routes.WalletSetup))
                        dispatcher.Dispatch(new GoAction(Routes.WalletSetup));
                    break;
                }
            }

            await _BTCPayAppConfigManager.UpdateConfig(action.Config);
        }
    }
    
    
    protected class WalletConfigLoadedActionEffect : Effect<WalletConfigLoadedAction>
    {
        private readonly NavigationManager _navigationManager;
        private readonly BTCPayAppConfigManager _BTCPayAppConfigManager;
        private readonly IState<RootState> _state;

        public WalletConfigLoadedActionEffect(NavigationManager navigationManager,
            BTCPayAppConfigManager BTCPayAppConfigManager, IState<RootState>state)
        {
            _navigationManager = navigationManager;
            _BTCPayAppConfigManager = BTCPayAppConfigManager;
            _state = state;
        }

        public override async Task HandleAsync(WalletConfigLoadedAction action, IDispatcher dispatcher)
        {
            if (action.Config is null && _state.Value.PairConfig is not null && !_navigationManager.Uri.EndsWith(Routes.WalletSetup))
                dispatcher.Dispatch(new GoAction(Routes.FirstRun));
            await _BTCPayAppConfigManager.UpdateConfig(action.Config);
        }
    }
}