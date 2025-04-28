using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKSpendableOutputEventHandler(OutputSweeper outputSweeper)
    : ILDKEventHandler<Event.Event_SpendableOutputs>
{
    public Task Handle(Event.Event_SpendableOutputs eventSpendableOutputs)
    {
        outputSweeper.track_spendable_outputs(eventSpendableOutputs.outputs, eventSpendableOutputs.channel_id, true,
            Option_u32Z.none());
        return Task.CompletedTask;
    }
}
// }
// public class LDKSpendableOutputEventHandler : ILDKEventHandler<Event.Event_SpendableOutputs>, IScopedHostedService
// {
//     private readonly BTCPayConnectionManager _connectionManager;
//     private readonly OnChainWalletManager _onChainWalletManager;
//     private readonly BTCPayAppServerClient _appServerClient;
//     private readonly LDKNode _node;
//     private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
//     private readonly OutputSweeper _outputSweeper;
//
//     public LDKSpendableOutputEventHandler(
//         BTCPayConnectionManager connectionManager,
//         OnChainWalletManager onChainWalletManager,
//         BTCPayAppServerClient appServerClient,
//         LDKNode node,
//         IDbContextFactory<AppDbContext> dbContextFactory, OutputSweeper outputSweeper)
//     {
//         _connectionManager = connectionManager;
//         _onChainWalletManager = onChainWalletManager;
//         _appServerClient = appServerClient;
//         _node = node;
//         _dbContextFactory = dbContextFactory;
//         _outputSweeper = outputSweeper;
//     }
//
//     public async Task Handle(Event.Event_SpendableOutputs eventSpendableOutputs)
//     {
//         _outputSweeper.track_spendable_outputs(eventSpendableOutputs.outputs, eventSpendableOutputs.channel_id, true,
//             Option_u32Z.none());
//
//     }
//     //
//     // var toPersist = eventSpendableOutputs.outputs.Where(descriptor => descriptor is not SpendableOutputDescriptor.SpendableOutputDescriptor_StaticOutput);
//     //
//     //     await PersistSpendableOutputs(toPersist);
//     // }
//     //
//     // private async Task PersistSpendableOutputs(IEnumerable<SpendableOutputDescriptor> toPersist)
//     // {
//     //
//     //     await using var context = await _dbContextFactory.CreateDbContextAsync();
//     //     List<Script> scripts = new();
//     //     var spendableOutputs =  toPersist.Select(descriptor =>
//     //     {
//     //         switch (descriptor)
//     //         {
//     //             case SpendableOutputDescriptor.SpendableOutputDescriptor_DelayedPaymentOutput delayedPaymentOutput:
//     //             {
//     //                 var txout = delayedPaymentOutput.delayed_payment_output.get_output().TxOut();
//     //                 scripts.Add(txout.ScriptPubKey);
//     //                 var outpoint = delayedPaymentOutput.delayed_payment_output.get_outpoint().Outpoint().ToString();
//     //                 return new SpendableCoin()
//     //                 {
//     //                     Outpoint = outpoint,
//     //                     Script = txout.ScriptPubKey.ToHex(),
//     //                     Data = descriptor.write()
//     //
//     //                 };
//     //             }
//     //             case SpendableOutputDescriptor.SpendableOutputDescriptor_StaticPaymentOutput staticPaymentOutput:
//     //             {
//     //                 var txout = staticPaymentOutput.static_payment_output.get_output().TxOut();
//     //                 scripts.Add(txout.ScriptPubKey);
//     //                 var outpoint = staticPaymentOutput.static_payment_output.get_outpoint().Outpoint().ToString();
//     //                 return new SpendableCoin()
//     //                 {
//     //                     Outpoint = outpoint,
//     //                     Script = txout.ScriptPubKey.ToHex(),
//     //                     Data = descriptor.write()
//     //                 };
//     //             }
//     //         }
//     //         return null;
//     //     }).Where(coin => coin is not null).Select(coin => coin!).ToArray();
//     //     await context.SpendableCoins.UpsertRange(spendableOutputs).NoUpdate().RunAsync();
//     //     await _node.TrackScripts(scripts.ToArray(),WalletDerivation.SpendableOutputs);
//     // }
//     //
//     // private Task AppServerClientOnOnNewBlock(object? sender, string e)
//     // {
//     //     throw new NotImplementedException();
//     // }
//     //
//     // private async Task AppServerClientOnTransactionDetected(object? sender, TransactionDetectedRequest transactionDetectedRequest)
//     // {
//     //     if (_onChainWalletManager.WalletConfig.Derivations[WalletDerivation.LightningScripts].Identifier ==
//     //         transactionDetectedRequest.Identifier)
//     //     {
//     //         await using var context = await _dbContextFactory.CreateDbContextAsync();
//     //
//     //         //find channels with the scripts
//     //         var channels = await context.LightningChannels
//     //             .Where(channel => channel.FundingScript != null &&
//     //                               transactionDetectedRequest.SpentScripts.Contains(channel.FundingScript))
//     //             .ToListAsync();
//     //         if (channels.Any() is true)
//     //         {
//     //
//     //             var txs = await _connectionManager.HubProxy.FetchTxsAndTheirBlockHeads(new[]
//     //                 {transactionDetectedRequest.TxId});
//     //             var tx = txs.Txs[transactionDetectedRequest.TxId];
//     //             var txParsed = Transaction.Parse(tx.Transaction, _onChainWalletManager.Network);
//     //             var scripts = txParsed.Outputs.Select(x => x.ScriptPubKey).ToArray();
//     //             await _node.TrackScripts(scripts, WalletDerivation.SpendableOutputs);
//     //
//     //         }
//     //     }
//     //     // }else if(_onChainWalletManager.WalletConfig.Derivations[WalletDerivation.SpendableOutputs].Identifier != e.identifier)
//     //     //     return;
//     //     //
//     //     //
//     //     // await using var context = await _dbContextFactory.CreateDbContextAsync();
//     //     // var spendableCoins = await context.SpendableCoins
//     //     //     .Where(coin => e.relatedScripts.Any(script => e.relatedScripts.Contains(coin.Script)))
//     //     //     .ToListAsync();
//     //     //
//     //     // if(spendableCoins.Any() is not true)
//     //     //     return;
//     //     //
//     //     //
//     //     // tx.Txs.TryGetValue(e.txId, out var transactionResponse);
//     //     //
//     //     //
//     //     // var spendableOutputDescriptors = spendableCoins.Select(coin => (coin, SpendableOutputDescriptor.read(coin.Data))).ToArray();
//     //     //
//     //
//     //
//     // }
//     //
//     // public async Task StartAsync(CancellationToken cancellationToken)
//     // {
//     //     _appServerClient.OnTransactionDetected += AppServerClientOnTransactionDetected;
//     //     _appServerClient.OnNewBlock += AppServerClientOnOnNewBlock;
//     // }
//     //
//     //
//     //
//     // public async Task StopAsync(CancellationToken cancellationToken)
//     // {
//     //     _appServerClient.OnTransactionDetected -= AppServerClientOnTransactionDetected;
//     //     _appServerClient.OnNewBlock -= AppServerClientOnOnNewBlock;
//     // }
// }
