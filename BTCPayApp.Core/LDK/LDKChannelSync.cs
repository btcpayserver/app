using BTCPayApp.CommonServer;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKChannelSync : IScopedHostedService, IDisposable
{
    private readonly Confirm[] _confirms;
    private readonly BTCPayConnectionManager _connectionManager;
    private readonly LDKNode _node;
    private readonly Network _network;
    private readonly Watch _watch;
    private readonly BTCPayAppServerClient _appHubClient;
    private readonly ILogger<LDKChannelSync> _logger;
    private readonly List<IDisposable> _disposables = new();
    

    public LDKChannelSync(
        IEnumerable<Confirm> confirms,
        BTCPayConnectionManager connectionManager,
        LDKNode node,
        Network network,
        Watch watch,
        BTCPayAppServerClient appHubClient,
        ILogger<LDKChannelSync> logger)
    {
        _confirms = confirms.ToArray();
        _connectionManager = connectionManager;
        _node = node;
        _network = network;
        _watch = watch;
        _appHubClient = appHubClient;
        _logger = logger;
        
    }

    private async Task LOlz(uint256[] txIds = null)
    {
        if (txIds is null)
        {
            
            var channels = await _node.GetChannels();
            txIds = channels.Select(zz => new uint256(zz.get_funding_txo().get_txid())).ToArray();
        }
        var txs1 = _confirms.SelectMany(confirm => confirm.get_relevant_txids().Select(zz =>
            (TransactionId: new uint256(zz.get_a()),
                Height: zz.get_b(),
                Block: zz.get_c() is Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some some
                    ? new uint256(some.some)
                    : null))).ToDictionary(tuple => tuple.TransactionId, tuple => tuple);

        foreach (var txid in txIds)
        {
            txs1.TryAdd(txid, (txid, 0, null));

        }
        _logger.LogInformation($"Fetching {txs1.Count} transactions");
        var result = await _connectionManager.HubProxy.FetchTxsAndTheirBlockHeads(txs1.Select(zz => zz.Key.ToString()).ToArray());
        _logger.LogInformation($"Fetched {result.Txs.Count} transactions");
        
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
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _disposables.Clear();
        var monitors = await _node.GetInitialChannelMonitors();
        var txsToCheck = monitors.Select(zz => zz.get_funding_txo().get_a().Outpoint().Hash).ToArray();
        foreach (var channelMonitor in monitors)
        {
            _watch.watch_channel(channelMonitor.get_funding_txo().get_a(), channelMonitor);
        }

        await LOlz(txsToCheck);
        
        var bb = await  _connectionManager.HubProxy.GetBestBlock();
       var bbHeader = BlockHeader.Parse( bb.BlockHeader, _network).ToBytes();
        foreach (var confirm in _confirms)
        {
            confirm.best_block_updated(bbHeader, bb.BlockHeight);
        }

        
        
        _disposables.Add(ChannelExtensions.SubscribeToEventWithChannelQueue<string>(
            action =>_appHubClient.OnNewBlock += action,
            action => _appHubClient.OnNewBlock -= action, OnNewBlock,
            cancellationToken)); 
        
        _disposables.Add(ChannelExtensions.SubscribeToEventWithChannelQueue<TransactionDetectedRequest>(
            action => _appHubClient.OnTransactionDetected += action,
            action => _appHubClient.OnTransactionDetected -= action, OnTransactionUpdate,
            cancellationToken));
    }

  

    private async Task OnTransactionUpdate( TransactionDetectedRequest txUpdate , CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Transaction update {txUpdate.TxId}");
        var txResult = await  _connectionManager.HubProxy.FetchTxsAndTheirBlockHeads(new[] {txUpdate.TxId});
        var tx = txResult.Txs[txUpdate.TxId];

        var txHash = new uint256(txUpdate.TxId);
        var txHashBytes = txHash.ToBytes();
        
        byte[]? headerBytes = null;
        int? blockHeight = null;
        if (tx.BlockHash is not null )
        {
            _logger.LogInformation($"Transaction {txUpdate.TxId} is confirmed");
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
        _logger.LogInformation($"Transaction update {txUpdate.TxId} processed");
        
    }
    private async Task OnNewBlock(string e, CancellationToken arg2)
    {
        _logger.LogInformation($"New block {e}");
        var blockHeaderResponse = await _connectionManager.HubProxy.GetBestBlock();
        var header = BlockHeader.Parse(blockHeaderResponse.BlockHeader, _network);
        var headerBytes = header.ToBytes();
        foreach (var confirm in _confirms)
        {
            confirm.best_block_updated(headerBytes, blockHeaderResponse.BlockHeight);
        }
        _logger.LogInformation($"New block {e} processed");
    }


    public async Task StopAsync(CancellationToken cancellationToken)
    {
        
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}