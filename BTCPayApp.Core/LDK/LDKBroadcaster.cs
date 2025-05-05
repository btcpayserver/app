using BTCPayApp.Core.Wallet;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

/// <summary>
/// Enables LDK to broadcast transactions through BTCPayServer.
/// </summary>
public class LDKBroadcaster(
    Network network,
    IEnumerable<IBroadcastGateKeeper> broadcastGateKeepers,
    OnChainWalletManager onChainWalletManager)
    : BroadcasterInterfaceInterface
{
    public void broadcast_transactions(byte[][] txs)
    {
        List<Task> tasks = new();
        foreach (var tx in txs)
        {
            var loadedTx = Transaction.Load(tx, network);
            if(broadcastGateKeepers.Any(gk => gk.DontBroadcast(loadedTx)))
                continue;
            tasks.Add(Broadcast(loadedTx));
        }
        Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    public async Task Broadcast(Transaction transaction, CancellationToken cancellationToken = default)
    {
        await onChainWalletManager.BroadcastTransaction(transaction, cancellationToken);
    }
}

