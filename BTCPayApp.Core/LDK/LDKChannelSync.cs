using BTCPayApp.CommonServer;
using BTCPayApp.Core;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.LDK;
using NBitcoin;
using org.ldk.structs;

namespace nldksample.LDK;

public class LDKChannelSync : IScopedHostedService, IDisposable
{
    private readonly Confirm[] _confirms;
    private readonly BTCPayConnectionManager _connectionManager;
    private readonly CurrentWalletService _currentWalletService;
    private readonly Network _network;
    private readonly Watch _watch;
    private readonly BTCPayAppServerClient _appHubClient;
    private List<IDisposable> _disposables = new();
    

    public LDKChannelSync(
        IEnumerable<Confirm> confirms,
        BTCPayConnectionManager connectionManager,
        CurrentWalletService currentWalletService,
        Network network,
        Watch watch,
        BTCPayAppServerClient appHubClient)
    {
        _confirms = confirms.ToArray();
        _connectionManager = connectionManager;
        _currentWalletService = currentWalletService;
        _network = network;
        _watch = watch;
        _appHubClient = appHubClient;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _disposables.Clear();
        var txs1 = _confirms.SelectMany(confirm => confirm.get_relevant_txids().Select(zz =>
            (TransactionId: new uint256(zz.get_a()),
                Height: zz.get_b(),
                Block: zz.get_c() is Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some some
                    ? new uint256(some.some)
                    : null))).ToDictionary(tuple => tuple.TransactionId, tuple => tuple);

        var result = await _connectionManager.HubProxy.FetchTxsAndTheirBlockHeads(txs1.Select(zz => zz.Key.ToString()).ToArray());
        
        
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

        var bb = await  _connectionManager.HubProxy.GetBestBlock();
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
        
        _disposables.Add(ChannelExtensions.SubscribeToEventWithChannelQueue<string>(
            action =>_appHubClient.OnNewBlock += action,
            action => _appHubClient.OnNewBlock -= action, OnNewBlock,
            cancellationToken)); 
        
        _disposables.Add(ChannelExtensions.SubscribeToEventWithChannelQueue<string>(
            action => _appHubClient.OnTransactionDetected += action,
            action => _appHubClient.OnTransactionDetected -= action, OnTransactionUpdate,
            cancellationToken));
    }

  

    private async Task OnTransactionUpdate(string txUpdate, CancellationToken cancellationToken)
    {
        var txResult = await  _connectionManager.HubProxy.FetchTxsAndTheirBlockHeads(new[] {txUpdate});
        var tx = txResult.Txs[txUpdate];

        var txHash = new uint256(txUpdate);
        var txHashBytes = txHash.ToBytes();
        
        byte[]? headerBytes = null;
        int? blockHeight = null;
        if (tx.BlockHash is not null )
        {
            var header = txResult.Blocks[tx.BlockHash];
            blockHeight = txResult.BlockHeghts[tx.BlockHash];
            headerBytes = BlockHeader.Parse(header, _network).ToBytes();
        }

        foreach (var confirm in _confirms)
        {
            if (blockHeight is null)
                confirm.transaction_unconfirmed(txHashBytes);
            else
                confirm.transactions_confirmed(headerBytes, [TwoTuple_usizeTransactionZ.of(1, txHashBytes)],blockHeight.Value);
        }
    }
    private async Task OnNewBlock(string e, CancellationToken arg2)
    {
        var blockHeaderResponse = await _connectionManager.HubProxy.GetBestBlock();
        var header = BlockHeader.Parse(blockHeaderResponse.BlockHeader, _network);
        var headerBytes = header.ToBytes();
        foreach (var confirm in _confirms)
        {
            confirm.best_block_updated(headerBytes, blockHeaderResponse.BlockHeight);
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