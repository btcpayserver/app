using BTCPayApp.Core.LDK;
using NBitcoin;
using org.ldk.structs;
using Transaction = NBitcoin.Transaction;

namespace nldksample.LDK;

public class LDKCoinSelector : CoinSelectionSourceInterface
{
    private readonly CurrentWalletService _currentWalletService;
    private readonly Network _network;

    public LDKCoinSelector(Network network, CurrentWalletService currentWalletService)
    {
        _currentWalletService = currentWalletService;
        _network = network;
    }
    
    
    public Result_CoinSelectionNoneZ select_confirmed_utxos(byte[] claim_id, Input[] must_spend,
        org.ldk.structs.TxOut[] must_pay_to,
        int target_feerate_sat_per_1000_weight)
    {
        var feerate = new FeeRate(Money.Satoshis(target_feerate_sat_per_1000_weight));
        var txouts = must_pay_to.Select(x => x.TxOut()).ToList();
        var coins = must_spend.Select(x => x.Coin()).ToList();
        var tx = _currentWalletService.CreateTransaction(
            txouts, feerate,
            coins).GetAwaiter().GetResult();
        if (tx is null)
            return Result_CoinSelectionNoneZ.err();

        var changeTxOut = tx.Value.Tx.Outputs.FirstOrDefault(@out => @out.ScriptPubKey == tx.Value.Change);
        
        var utxos = tx.Value.SpentCoins.Select(x => Utxo.of(x.Outpoint.Outpoint(), x.TxOut.TxOut(), tx.Value.Tx.Inputs.First(@in => @in.PrevOut == x.Outpoint).GetSerializedSize())).ToArray();
        return Result_CoinSelectionNoneZ.ok(CoinSelection.of(utxos, changeTxOut is null ? Option_TxOutZ.none() : Option_TxOutZ.some(changeTxOut.TxOut())));
    }

    public Result_TransactionNoneZ sign_psbt(byte[] psbt)
    {
       var psbtParsed =  PSBT.Load(psbt, _network);
       return sign_tx(psbtParsed.GetGlobalTransaction().ToBytes());
    }

    public Result_TransactionNoneZ sign_tx(byte[] tx)
    {
        var tx1 =  Transaction.Load(tx, _network);
        tx1 = _currentWalletService.SignTransaction(_currentWalletService.CurrentWallet,tx1).GetAwaiter().GetResult();
        return Result_TransactionNoneZ.ok(tx1.ToBytes());
    }
}