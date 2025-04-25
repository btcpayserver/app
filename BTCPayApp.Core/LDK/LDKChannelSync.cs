using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.Wallet;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

/// <summary>
/// a background service that keeps LDK synchronized with onchain events that it needs to know about
/// </summary>
public class LDKChannelSync(
    IEnumerable<Confirm> confirms,
    BTCPayConnectionManager connectionManager,
    OnChainWalletManager onchainWalletManager,
    LDKNode node,
    Network network,
    Watch watch,
    LDKFilter ldkFilter,
    BTCPayAppServerClient appHubClient,
    ILogger<LDKChannelSync> logger)
    : IScopedHostedService, IDisposable
{
    private readonly Confirm[] _confirms = confirms.ToArray();
    private readonly List<IDisposable> _disposables = [];

    /// <summary>
    ///
    /// </summary>
    /// <param name="txIds">The specific transaction ids we should check the status of. If null, we get a list of transaction ids from LDK, and also a list of utxos that we are watching </param>
    private async Task PollForTransactionUpdates(uint256[]? txIds = null)
    {
        Dictionary<uint256, (uint256 TransactionId, int Height, uint256? Block)> relevantTransactionsFromConfirms;
        List<LDKWatchedOutput> watchedOutputs = [];
        List<LDKWatchedOutput> spentWatchedOutputs = [];

        if (txIds is null)
        {
            txIds = [];

            watchedOutputs = await ldkFilter.GetWatchedOutputs();

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
            relevantTransactionsFromConfirms = txIds.ToDictionary(zz => zz,
                (uint256 TransactionId, int Height, uint256? Block) (uint256) => (uint256, 0, null));
        }

        logger.LogInformation("Fetching {Count} transactions", relevantTransactionsFromConfirms.Count);
        var txIdsToQuery = relevantTransactionsFromConfirms.Select(zz => zz.Key.ToString()).ToArray();
        var outpoints = watchedOutputs.Select(zz => zz.Outpoint.ToString()).ToArray();
        var lnIdentifier = await node.Identifier;
        var result =
            await connectionManager.HubProxy.FetchTxsAndTheirBlockHeads(lnIdentifier, txIdsToQuery, outpoints);
        var blockHeaders =
            result.BlockHeaders.ToDictionary(zz => new uint256(zz.Key), zz => BlockHeader.Parse(zz.Value, network));
        var txs = result.Txs.ToDictionary(zz => new uint256(zz.Key),
            zz => Transaction.Parse(zz.Value.Transaction, network));

        logger.LogInformation("Fetched {Count} transactions", result.Txs.Count);
        Dictionary<uint256, List<TwoTuple_usizeTransactionZ>> blockToTxList = new();

        foreach (var transactionResult in result.Txs)
        {
            var tx = txs[new uint256(transactionResult.Key)];
            if (relevantTransactionsFromConfirms.TryGetValue(new uint256(transactionResult.Key), out var tx1))
            {
                switch (tx1.Block)
                {
                    case null when transactionResult.Value.BlockHash is null:
                        if (tx1.Block != null)
                            foreach (var confirm in _confirms)
                            {
                                confirm.transaction_unconfirmed(tx1.TransactionId.ToBytes());
                            }

                        continue;
                    case null when transactionResult.Value.BlockHash is not null:
                    {
                        blockToTxList.TryAdd(new uint256(transactionResult.Value.BlockHash),
                            new List<TwoTuple_usizeTransactionZ>());

                        var list = blockToTxList[new uint256(transactionResult.Value.BlockHash)];
                        list.Add(TwoTuple_usizeTransactionZ.of(0, tx.ToBytes()));
                        break;
                    }
                    case not null when transactionResult.Value.BlockHash is not null &&
                                       tx1.Block != uint256.Parse(transactionResult.Value.BlockHash):
                    {
                        foreach (var confirm in _confirms)
                        {
                            confirm.transaction_unconfirmed(tx1.TransactionId.ToBytes());
                        }

                        blockToTxList.TryAdd(new uint256(transactionResult.Value.BlockHash), []);

                        var list = blockToTxList[new uint256(transactionResult.Value.BlockHash)];
                        list.Add(TwoTuple_usizeTransactionZ.of(0, tx.ToBytes()));
                        break;
                    }
                }
            }
            else if (tx.Inputs.Any(zz => watchedOutputs.Any(zzz => zzz.Outpoint == zz.PrevOut)) &&
                     transactionResult.Value.BlockHash is not null)
            {
                var watchedOutput = watchedOutputs.First(zz => tx.Inputs.Any(zzz => zzz.PrevOut == zz.Outpoint));
                blockToTxList.TryAdd(new uint256(transactionResult.Value.BlockHash), []);
                var list = blockToTxList[new uint256(transactionResult.Value.BlockHash)];
                list.Add(TwoTuple_usizeTransactionZ.of(0, tx.ToBytes()));

                spentWatchedOutputs.Add(watchedOutput);
            }
        }

        foreach (var block in blockToTxList)
        {
            var header = blockHeaders[block.Key];
            var height = result.BlockHeights[block.Key.ToString()];
            var headerBytes = header.ToBytes();
            // if(block.Key.ToString() == "00000000000000086130942075335f4937cd89cb183d69cce612eb780c838f7c")
            //     continue;
            foreach (var confirm in _confirms)
            {
                confirm.transactions_confirmed(headerBytes, block.Value.ToArray(), height);
            }
        }

        await ldkFilter.OutputsSpent(spentWatchedOutputs);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _disposables.Clear();
        var monitors = await node.GetInitialChannelMonitors();
        foreach (var channelMonitor in monitors)
        {
            watch.watch_channel(channelMonitor.get_funding_txo().get_a(), channelMonitor);
        }

        await PollForTransactionUpdates();

        var bb = await onchainWalletManager.GetBestBlock();
        var bbHeader = BlockHeader.Parse(bb.BlockHeader, network).ToBytes();
        foreach (var confirm in _confirms)
        {
            confirm.best_block_updated(bbHeader, bb.BlockHeight);
        }

        _disposables.Add(ChannelExtensions.SubscribeToEventWithChannelQueue<string>(
            action => appHubClient.OnNewBlock += action,
            action => appHubClient.OnNewBlock -= action, OnNewBlock,
            cancellationToken));

        _disposables.Add(ChannelExtensions.SubscribeToEventWithChannelQueue<TransactionDetectedRequest>(
            action => appHubClient.OnTransactionDetected += action,
            action => appHubClient.OnTransactionDetected -= action, OnTransactionUpdate,
            cancellationToken));
    }

    private async Task OnTransactionUpdate(TransactionDetectedRequest txUpdate, CancellationToken cancellationToken)
    {
        logger.LogInformation("Transaction update {TxId}", txUpdate.TxId);

        await PollForTransactionUpdates([new uint256(txUpdate.TxId)]);
        logger.LogInformation("Transaction update {TxId} processed", txUpdate.TxId);
    }

    private async Task OnNewBlock(string block, CancellationToken arg2)
    {
        logger.LogInformation("New block {Block}", block);

        var blockHeaderResponse = await onchainWalletManager.GetBestBlock();
        var header = BlockHeader.Parse(blockHeaderResponse.BlockHeader, network);
        var headerBytes = header.ToBytes();
        foreach (var confirm in _confirms)
        {
            confirm.best_block_updated(headerBytes, blockHeaderResponse.BlockHeight);
        }

        await PollForTransactionUpdates();
        logger.LogInformation("New block {Block} processed", block);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
