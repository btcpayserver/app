using BTCPayApp.Core;
using BTCPayServer.Client.Models;
using Fluxor;
using Microsoft.AspNetCore.SignalR.Client;
using NBitcoin;

namespace BTCPayApp.UI.Features;

public enum OnChainTransactionStatus
{
    Mempool,
    Confirmed
}

public record OnChainTransaction(string Id, decimal Balance, DateTimeOffset Timestamp, OnChainTransactionStatus Status);

[FeatureState]
public record OnChainTransactionsState(Dictionary<string, OnChainTransaction>? AllTransactions, bool Error)
{
    public record SetError(bool Error);

    public record UpdateTransactionAction(OnChainTransaction Transaction);

    public record AllTransactionsLoadedAction(IEnumerable<OnChainTransaction> Transactions);

    private OnChainTransactionsState() : this(null, false)
    {
    }

    protected class BTCPayConnectionUpdatedActionEffect : Effect<RootState.BTCPayConnectionUpdatedAction>
    {
        private readonly IState<OnChainTransactionsState> _state;


        public BTCPayConnectionUpdatedActionEffect(
            IState<OnChainTransactionsState> state)
        {
            _state = state;
        }

        public override async Task HandleAsync(RootState.BTCPayConnectionUpdatedAction action, IDispatcher dispatcher)
        {
            if (action.ConnectionState is HubConnectionState.Connected)
            {
                dispatcher.Dispatch(new LoadTransactionsAction(true));
            }
        }
    }

    public record LoadTransactionsAction(bool IgnoreIfNotNull = false);

    protected class LoadTransactionsActionEffect : Effect<LoadTransactionsAction>
    {
        private readonly IState<RootState> _rootState;
        private readonly IState<OnChainTransactionsState> _state;
        private readonly BTCPayConnection _connection;

        public LoadTransactionsActionEffect(IState<RootState> rootState, IState<OnChainTransactionsState> state,
            BTCPayConnection connection)
        {
            _rootState = rootState;
            _state = state;
            _connection = connection;
        }

        public override async Task HandleAsync(LoadTransactionsAction action, IDispatcher dispatcher)
        {
            if (_rootState.Value.Loading.TryGetValue(RootState.LoadingHandles.TransactionState, out _))
                return;
            if (action.IgnoreIfNotNull && _state.Value.AllTransactions is not null)
                return;
            dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.TransactionState, true));


            try
            {
                var txs = await _connection.Client.ShowOnChainWalletTransactions(
                    _rootState.Value.PairConfig.PairingResult.StoreId,
                    "BTC", null, null);
                dispatcher.Dispatch(new AllTransactionsLoadedAction(txs.Select(t =>
                    new OnChainTransaction(t.TransactionHash.ToString()!, t.Amount, t.Timestamp,
                        t.Status == TransactionStatus.Unconfirmed
                            ? OnChainTransactionStatus.Mempool
                            : OnChainTransactionStatus.Confirmed))));
            }
            catch (Exception e)
            {
                dispatcher.Dispatch(new SetError(true));
            }
            finally
            {
                dispatcher.Dispatch(new RootState.LoadingAction(RootState.LoadingHandles.TransactionState, false));
            }
        }
    }


    protected class AllTransactionsLoadedReducer : Reducer<OnChainTransactionsState, AllTransactionsLoadedAction>
    {
        public override OnChainTransactionsState Reduce(OnChainTransactionsState state,
            AllTransactionsLoadedAction action)
        {
            return state with {AllTransactions = action.Transactions.ToDictionary(t => t.Id)};
        }
    }

    protected class UpdateTransactionReducer : Reducer<OnChainTransactionsState, UpdateTransactionAction>
    {
        public override OnChainTransactionsState Reduce(OnChainTransactionsState state, UpdateTransactionAction action)
        {
            var allTransactions = state.AllTransactions ?? new Dictionary<string, OnChainTransaction>();
            allTransactions.AddOrReplace(action.Transaction.Id, action.Transaction);
            return state with {AllTransactions = allTransactions};
        }
    }
}