using BTCPayApp.Core;
using BTCPayApp.Core.LDK;
using NBitcoin;
using org.ldk.structs;

namespace nldksample.LDK;

public class LDKChannelSync : IScopedHostedService, IDisposable
{
    private readonly Confirm[] _confirms;
    private readonly BTCPayConnection _connection;
    private readonly CurrentWalletService _currentWalletService;
    private readonly Network _network;
    private readonly Watch _watch;
    private List<IDisposable> _disposables = new();
    

    public LDKChannelSync(
        IEnumerable<Confirm> confirms,
        BTCPayConnection connection,
        CurrentWalletService currentWalletService,
        Network network,
        Watch watch)
    {
        _confirms = confirms.ToArray();
        _connection = connection;
        _currentWalletService = currentWalletService;
        _network = network;
        _watch = watch;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var txs1 = _confirms.SelectMany(confirm => confirm.get_relevant_txids().Select(zz =>
            (TransactionId: new uint256(zz.get_a()),
                Height: zz.get_b(),
                Block: zz.get_c() is Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some some
                    ? new uint256(some.some)
                    : null))).ToDictionary(tuple => tuple.TransactionId, tuple => tuple);

        var result = await _connection.HubProxy.FetchTxsAndTheirBlockHeads(txs1.Select(zz => zz.Key.ToString()).ToArray());
        
        
        Dictionary<uint256, List<TwoTuple_usizeTransactionZ>> confirmedTxList = new();
        foreach (var transactionResult in result.Txs)
        {

            var tx1 = txs1[new uint256(transactionResult.Key)];

            if (tx1.Block is null && transactionResult.Value.BlockHash is null)
                continue;
            else if (tx1.Block is null && transactionResult.Value.BlockHash is not null)
            {
                var bh = new uint256(transactionResult.Value.BlockHash);
                confirmedTxList.TryAdd(bh, new List<TwoTuple_usizeTransactionZ>());

                var txList = confirmedTxList[bh];

                txList.Add(TwoTuple_usizeTransactionZ.of(1,
                    Transaction.Parse(transactionResult.Value.Transaction, _network).ToBytes()));
                continue;
            }
            else if (tx1.Block is not null && transactionResult.Value.BlockHash is null)
            {
                foreach (var confirm in _confirms)
                {
                    confirm.transaction_unconfirmed(tx1.TransactionId.ToBytes());
                }

                continue;
            }
        }

        foreach (var confirmedTxListItem in confirmedTxList)
        {
            
            var header = result.Blocks[confirmedTxListItem.Key.ToString()];
            var height = result.BlockHeghts[confirmedTxListItem.Key.ToString()];
            var headerBytes = BlockHeader.Parse(header, _network).ToBytes();
            foreach (var confirm in _confirms)
            {
                confirm.transactions_confirmed(headerBytes, confirmedTxListItem.Value.ToArray(),  height);
            }
        }

        var bb = await  _connection.HubProxy.GetBestBlock();
       var bbHeader = BlockHeader.Parse( bb.BlockHeader, _network).ToBytes();
        foreach (var confirm in _confirms)
        {
            confirm.best_block_updated(bbHeader, bb.BlockHeight);
        }

        var monitors = await _currentWalletService.GetInitialChannelMonitors();
        foreach (var channelMonitor in monitors)
        {
            _watch.watch_channel(channelMonitor.get_funding_txo().get_a(), channelMonitor);
        }
        _connection.
        
        _disposables.Add(ChannelExtensions.SubscribeToEventWithChannelQueue<NewBlockEvent>(
            action => _nbxListener.NewBlock += action,
            action => _nbxListener.NewBlock -= action, OnNewBlock,
            cancellationToken)); 
        
        _disposables.Add(ChannelExtensions.SubscribeToEventWithChannelQueue<TransactionUpdateEvent>(
            action => _nbxListener.TransactionUpdate += action,
            action => _nbxListener.TransactionUpdate -= action, OnTransactionUpdate,
            cancellationToken));
    }

  

    private async Task OnTransactionUpdate(TransactionUpdateEvent txUpdate, CancellationToken cancellationToken)
    {
        if (_currentWalletService.CurrentWallet != txUpdate.Wallet.Id)
            return;

        var tx = txUpdate.TransactionInformation.Transaction;
        var txHash = tx.GetHash();
        byte[]? headerBytes = null;
        if (txUpdate.TransactionInformation.Confirmations > 0)
        {
            var header = await _explorerClient.RPCClient
                .GetBlockHeaderAsync(txUpdate.TransactionInformation.BlockHash, CancellationToken.None);
            headerBytes = header.ToBytes();
        }

        foreach (var confirm in _confirms)
        {
            if (txUpdate.TransactionInformation.Confirmations == 0)
                confirm.transaction_unconfirmed(txHash.ToBytes());
            else
                confirm.transactions_confirmed(headerBytes, new[] {TwoTuple_usizeTransactionZ.of(1, tx.ToBytes()),},
                    (int) txUpdate.TransactionInformation.Height);
        }
    }
    private async Task OnNewBlock(NewBlockEvent e, CancellationToken arg2)
    {
        var header = await _explorerClient.RPCClient.GetBlockHeaderAsync(e.Hash, CancellationToken.None);
        var headerBytes = header.ToBytes();
        foreach (var confirm in _confirms)
        {
            confirm.best_block_updated(headerBytes, e.Height);
        }
    }


    public async Task StopAsync(CancellationToken cancellationToken)
    {
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}