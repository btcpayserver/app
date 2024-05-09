using BTCPayApp.Core.Attempt2;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKBroadcaster : BroadcasterInterfaceInterface
{
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly Network _network;
    private readonly IEnumerable<IBroadcastGateKeeper> _broadcastGateKeepers;

    public LDKBroadcaster(BTCPayConnectionManager btcPayConnectionManager, 
        Network network,
        IEnumerable<IBroadcastGateKeeper> broadcastGateKeepers)
    {
        _btcPayConnectionManager = btcPayConnectionManager;
        _network = network;
        _broadcastGateKeepers = broadcastGateKeepers;
    }

    public void broadcast_transactions(byte[][] txs)
    {
        List<Task> tasks = new();
        foreach (var tx in txs)
        {
            var loadedTx = Transaction.Load(tx, _network);
            if(_broadcastGateKeepers.Any(gk => gk.DontBroadcast(loadedTx)))
                continue;
            tasks.Add(Broadcast(loadedTx));
        }
        Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    public async Task Broadcast(Transaction transaction, CancellationToken cancellationToken = default)
    {
        await _btcPayConnectionManager.HubProxy.BroadcastTransaction(transaction.ToHex());
    }
}

