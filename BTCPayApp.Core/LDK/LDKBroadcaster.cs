using BTCPayApp.Core;
using NBitcoin;
using nldksample.LSP.Flow;
using org.ldk.structs;

namespace nldksample.LDK;

public class LDKBroadcaster : BroadcasterInterfaceInterface
{
    private readonly BTCPayConnection _btcPayConnection;
    private readonly Network _network;
    private readonly IEnumerable<IBroadcastGateKeeper> _broadcastGateKeepers;

    public LDKBroadcaster(BTCPayConnection btcPayConnection, 
        Network network,
        IEnumerable<IBroadcastGateKeeper> broadcastGateKeepers)
    {
        _btcPayConnection = btcPayConnection;
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
        await _btcPayConnection.HubProxy.BroadcastTransaction(transaction.ToHex());
    }
}

