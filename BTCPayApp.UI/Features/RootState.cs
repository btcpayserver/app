using BTCPayApp.Core;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Data;
using Fluxor;
using Microsoft.AspNetCore.SignalR.Client;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record RootState(
    HashSet<RootState.LoadingHandles> Loading,
    HubConnectionState? BTCPayServerConnectionState,
    LightningNodeState? LightningNodeState,
    OnChainWalletState? OnchainWalletState)
{
    public enum LoadingHandles
    {
        UiState,
        Connection,
        WalletState,
        LightningState
    }

    public RootState() : this(new HashSet<LoadingHandles>(), null,null, null)
    {
    }

    public record LoadingAction(LoadingHandles LoadingHandle, bool IsLoading);
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

    protected class OnChainWalletStateUpdatedReducer : Reducer<RootState, OnChainWalletStateUpdatedAction>
    {
        public override RootState Reduce(RootState state, OnChainWalletStateUpdatedAction action)
        {
            return state with { OnchainWalletState = action.State };
        }
    }

    public record OnChainWalletStateUpdatedAction(OnChainWalletState State);
}
