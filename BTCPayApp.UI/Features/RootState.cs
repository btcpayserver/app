using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Wallet;
using Fluxor;
using Fluxor.Blazor.Web.Middlewares.Routing;
using Microsoft.AspNetCore.Components;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record RootState
{
    public BTCPayConnectionState ConnectionState;
    public OnChainWalletState? OnchainWalletState;
    public LightningNodeState? LightningNodeState;

    public record ConnectionStateUpdatedAction(BTCPayConnectionState State);
    public record OnChainWalletStateUpdatedAction(OnChainWalletState State);
    public record LightningNodeStateUpdatedAction(LightningNodeState State);

    public class ConnectionEffects(NavigationManager navigationManager)
    {
        [EffectMethod]
        public Task HandleConnectionStateUpdatedAction(RootState.ConnectionStateUpdatedAction action, IDispatcher dispatcher)
        {
            if (action.State == BTCPayConnectionState.WaitingForEncryptionKey)
            {
                dispatcher.Dispatch(new GoAction(navigationManager.ToAbsoluteUri(Routes.Pairing).ToString()));
            }
            return Task.CompletedTask;
        }
    }

    protected class ConnectionUpdatedReducer : Reducer<RootState, ConnectionStateUpdatedAction>
    {
        public override RootState Reduce(RootState state, ConnectionStateUpdatedAction action)
        {
            return state with { ConnectionState = action.State };
        }
    }

    protected class OnChainWalletStateUpdatedReducer : Reducer<RootState, OnChainWalletStateUpdatedAction>
    {
        public override RootState Reduce(RootState state, OnChainWalletStateUpdatedAction action)
        {
            return state with { OnchainWalletState = action.State };
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
