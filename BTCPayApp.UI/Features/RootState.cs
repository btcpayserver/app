using BTCPayApp.Core;
using Fluxor;
using Microsoft.AspNetCore.SignalR.Client;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record RootState(
    HashSet<RootState.LoadingHandles> Loading,
    BTCPayPairConfig? PairConfig,
    WalletConfig? WalletConfig,
    HubConnectionState? BTCPayServerConnectionState,
    LightningNodeState LightningNodeState,
    bool PairConfigRequested,
    bool WalletConfigRequested)
{
    public enum LoadingHandles
    {
        UiState,
        PairConfig,
        WalletConfig,
        TransactionState,
        LightningState
    }

    public RootState() : this(new HashSet<LoadingHandles>(), null, null, null, LightningNodeState.NotConfigured, false, false)
    {
    }

    public record PairConfigLoadedAction(BTCPayPairConfig? Config);

    public record WalletConfigLoadedAction(WalletConfig? Config);

    public record LoadingAction(LoadingHandles LoadingHandle, bool IsLoading);

    protected class LoadingReducer : Reducer<RootState, LoadingAction>
    {
        public override RootState Reduce(RootState state, LoadingAction action)
        {
            var loading = state.Loading;
            var handle = action.LoadingHandle;
            _ = action.IsLoading ? loading.Add(handle) : loading.Remove(handle);
            return state with { Loading = loading };
        }
    }

    protected class PairConfigLoadedReducer : Reducer<RootState, PairConfigLoadedAction>
    {
        public override RootState Reduce(RootState state, PairConfigLoadedAction action)
        {
            var pairConfig = action.Config;
            return state with { PairConfig = pairConfig, PairConfigRequested = true };
        }
    }

    protected class WalletConfigLoadedReducer : Reducer<RootState, WalletConfigLoadedAction>
    {
        public override RootState Reduce(RootState state, WalletConfigLoadedAction action)
        {
            var walletConfig = action.Config;
            return state with { WalletConfig = walletConfig, WalletConfigRequested = true };
        }
    }

    protected class BTCPayConnectionUpdatedReducer : Reducer<RootState, BTCPayConnectionUpdatedAction>
    {
        public override RootState Reduce(RootState state, BTCPayConnectionUpdatedAction action)
        {
            return state with { BTCPayServerConnectionState = action.ConnectionState };
        }
    }

    protected class LightningNodeStateUpdatedReducer : Reducer<RootState, LightningNodeStateUpdatedAction>
    {
        public override RootState Reduce(RootState state, LightningNodeStateUpdatedAction action)
        {
            return state with { LightningNodeState = action.State };
        }
    }

    protected class PairConfigLoadedActionEffect : Effect<PairConfigLoadedAction>
    {
        private readonly BTCPayAppConfigManager _btcPayAppConfigManager;

        public PairConfigLoadedActionEffect(BTCPayAppConfigManager btcPayAppConfigManager)
        {
            _btcPayAppConfigManager = btcPayAppConfigManager;
        }

        public override async Task HandleAsync(PairConfigLoadedAction action, IDispatcher dispatcher)
        {
            await _btcPayAppConfigManager.UpdateConfig(action.Config);
        }
    }

    protected class WalletConfigLoadedActionEffect : Effect<WalletConfigLoadedAction>
    {
        private readonly BTCPayAppConfigManager _btcPayAppConfigManager;

        public WalletConfigLoadedActionEffect(BTCPayAppConfigManager btcPayAppConfigManager)
        {
            _btcPayAppConfigManager = btcPayAppConfigManager;
        }

        public override async Task HandleAsync(WalletConfigLoadedAction action, IDispatcher dispatcher)
        {
            await _btcPayAppConfigManager.UpdateConfig(action.Config);
        }
    }

    public record BTCPayConnectionUpdatedAction(HubConnectionState? ConnectionState);
    public record LightningNodeStateUpdatedAction(LightningNodeState State);
}
