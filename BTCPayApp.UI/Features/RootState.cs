using BTCPayApp.Core;
using BTCPayApp.Core.Data;
using Fluxor;
using Microsoft.AspNetCore.SignalR.Client;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record RootState(
    HashSet<RootState.LoadingHandles> Loading,
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
        LightningState
    }

    public RootState() : this(new HashSet<LoadingHandles>(), null, null, LightningNodeState.NotConfigured, false, false)
    {
    }

    public record LoadingAction(LoadingHandles LoadingHandle, bool IsLoading);
    public record WalletConfigLoadedAction(WalletConfig? Config);
    public record WalletConfigChangeRequestedAction(WalletConfig Config);
    public record BTCPayConnectionUpdatedAction(HubConnectionState? ConnectionState);
    public record LightningNodeStateUpdatedAction(LightningNodeState State);

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


}
