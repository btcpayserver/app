using BTCPayApp.CommonServer;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKChannelSync : IScopedHostedService, IDisposable
{
    private readonly Confirm[] _confirms;
    private readonly BTCPayConnectionManager _connectionManager;
    private readonly OnChainWalletManager _onchainWalletManager;
    private readonly LDKNode _node;
    private readonly Network _network;
    private readonly Watch _watch;
    private readonly LDKFilter _ldkFilter;
    private readonly BTCPayAppServerClient _appHubClient;
    private readonly ILogger<LDKChannelSync> _logger;
    private readonly IConfigProvider _configProvider;
    private readonly List<IDisposable> _disposables = new();
    

    public LDKChannelSync(
        IEnumerable<Confirm> confirms,
        BTCPayConnectionManager connectionManager,
        OnChainWalletManager onchainWalletManager,
        LDKNode node,
        Network network,
        Watch watch,
        LDKFilter ldkFilter,
        BTCPayAppServerClient appHubClient,
        ILogger<LDKChannelSync> logger, IConfigProvider configProvider)
    {
        _confirms = confirms.ToArray();
        _connectionManager = connectionManager;
        _onchainWalletManager = onchainWalletManager;
        _node = node;
        _network = network;
        _watch = watch;
        _ldkFilter = ldkFilter;
        _appHubClient = appHubClient;
        _logger = logger;
        _configProvider = configProvider;
    }

    private async Task PollForTransactionUpdates(uint256[]? txIds = null)
    {
        Dictionary<uint256, (uint256 TransactionId, int Height, uint256? Block)> relevantTransactionsFromConfirms;
        List<LDKWatchedOutput> watchedOutputs= new();
        List<LDKWatchedOutput> spentWatchedOutputs= new();
        
        
        if (txIds is null)
        {
            txIds = [];

            watchedOutputs = await _ldkFilter.GetWatchedOutputs();

            relevantTransactionsFromConfirms = _confirms.SelectMany(confirm => confirm.get_relevant_txids().Select(zz =>
                (TransactionId: new uint256(zz.get_a()),
                    Height: zz.get_b(),
                    Block: zz.get_c() is Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some some
                        ? new uint256(some.some)
                        : null))).ToDictionary(tuple => tuple.TransactionId, tuple => tuple);

            foreach (var txid in txIds)
            {
                relevantTransactionsFromConfirms.TryAdd(txid, (txid, 0, null));

            }
        }
        else
        {
            relevantTransactionsFromConfirms = txIds.ToDictionary(zz => zz, (uint256 TransactionId, int Height, uint256? Block) (uint256) => (uint256, 0, null));

        }
       
        _logger.LogInformation($"Fetching {relevantTransactionsFromConfirms.Count} transactions");
        var txIdsToQuery = relevantTransactionsFromConfirms.Select(zz => zz.Key.ToString()).ToArray();
        var outpoints = watchedOutputs.Select(zz => zz.Outpoint.ToString()).ToArray();
        var result = await _connectionManager.HubProxy.FetchTxsAndTheirBlockHeads(_node.Identifier, txIdsToQuery, outpoints).RunInOtherThread();
         var blockHeaders = result.BlockHeaders.ToDictionary(zz => new uint256(zz.Key), zz => BlockHeader.Parse(zz.Value, _network));
        var txs = result.Txs.ToDictionary(zz => new uint256(zz.Key), zz => Transaction.Parse(zz.Value.Transaction, _network));
        
        
        _logger.LogInformation($"Fetched {result.Txs.Count} transactions");
        Dictionary<uint256, List<TwoTuple_usizeTransactionZ>> blockToTxList = new();
        
        
        // Dictionary<uint256, List<TwoTuple_usizeTransactionZ>> confirmedTxList = new();
        foreach (var transactionResult in result.Txs)
        {
            var tx = txs[new uint256(transactionResult.Key)];
            if(relevantTransactionsFromConfirms.TryGetValue(new uint256(transactionResult.Key), out var tx1))
            {
                switch (tx1.Block)
                {
                    case null when transactionResult.Value.BlockHash is null:
                        continue;
                    case null when transactionResult.Value.BlockHash is not null:
                    {
                        blockToTxList.TryAdd(new uint256(transactionResult.Value.BlockHash), new List<TwoTuple_usizeTransactionZ>());
              
                        var list = blockToTxList[new uint256(transactionResult.Value.BlockHash)];
                        list.Add(TwoTuple_usizeTransactionZ.of(0,tx.ToBytes()));
                        break;
                    }
                    case { } when transactionResult.Value.BlockHash is not null:
                    {
                        foreach (var confirm in _confirms)
                        {
                            confirm.transaction_unconfirmed(tx1.TransactionId.ToBytes());
                        }
                        break;
                    }
                }
            }
            else if (tx.Inputs.Any(zz => watchedOutputs.Any(zzz => zzz.Outpoint == zz.PrevOut)) && transactionResult.Value.BlockHash is not null)
            {
                var watchedOutput = watchedOutputs.First(zz => tx.Inputs.Any(zzz => zzz.PrevOut == zz.Outpoint));
                blockToTxList.TryAdd(new uint256(transactionResult.Value.BlockHash), new List<TwoTuple_usizeTransactionZ>());
                var list = blockToTxList[new uint256(transactionResult.Value.BlockHash)];
                list.Add(TwoTuple_usizeTransactionZ.of(0,tx.ToBytes()));
                
                spentWatchedOutputs.Add(watchedOutput);
            }
            
        }
        foreach (var block in blockToTxList)
        {
            var header = blockHeaders[block.Key];
            var height = result.BlockHeghts[block.Key.ToString()];
            var headerBytes = header.ToBytes();
            // if(block.Key.ToString() == "00000000000000086130942075335f4937cd89cb183d69cce612eb780c838f7c")
            //     continue;
            foreach (var confirm in _confirms)
            {
                confirm.transactions_confirmed(headerBytes, block.Value.ToArray(),height);
            }
        }

        await _ldkFilter.OutputsSpent(spentWatchedOutputs);
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _disposables.Clear();
        var monitors = await _node.GetInitialChannelMonitors();
        foreach (var channelMonitor in monitors)
        {
            _watch.watch_channel(channelMonitor.get_funding_txo().get_a(), channelMonitor);
        }

        await PollForTransactionUpdates();

        var bb = await _onchainWalletManager.GetBestBlock();
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

        await PollForTransactionUpdates([new uint256(txUpdate.TxId)]);
        _logger.LogInformation($"Transaction update {txUpdate.TxId} processed");
        
    }
    private async Task OnNewBlock(string e, CancellationToken arg2)
    {
        _logger.LogInformation($"New block {e}");
       
        var blockHeaderResponse = await  _onchainWalletManager.GetBestBlock();
        var header = BlockHeader.Parse(blockHeaderResponse.BlockHeader, _network);
        var headerBytes = header.ToBytes();
        foreach (var confirm in _confirms)
        {
            confirm.best_block_updated(headerBytes, blockHeaderResponse.BlockHeight);
        }
        await PollForTransactionUpdates();
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