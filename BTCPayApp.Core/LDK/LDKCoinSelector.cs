using BTCPayApp.Core.Wallet;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKCoinSelector(OnChainWalletManager onChainWalletManager) : CoinSelectionSourceInterface
{
    public Result_CoinSelectionNoneZ select_confirmed_utxos(byte[] claim_id, Input[] must_spend,
        org.ldk.structs.TxOut[] must_pay_to,
        int target_feerate_sat_per_1000_weight)
    {
        var feerate = new FeeRate(Money.Satoshis(target_feerate_sat_per_1000_weight));
        var txouts = must_pay_to.Select(x => x.TxOut()).ToList();
        var coins = must_spend.Select(x => x.Coin()).ToList();
        try
        {
            var tx = onChainWalletManager.CreateTransaction(
                txouts, feerate,
                coins).GetAwaiter().GetResult();

            var changeTxOut = tx.Tx.Outputs.FirstOrDefault(@out => @out.ScriptPubKey == tx.Change.ScriptPubKey);

            var utxos = tx.SpentCoins.Select(x => Utxo.of(x.Outpoint.Outpoint(), x.TxOut.TxOut(), tx.Tx.Inputs.First(@in => @in.PrevOut == x.Outpoint).GetSerializedSize())).ToArray();
            return Result_CoinSelectionNoneZ.ok(CoinSelection.of(utxos, changeTxOut is null ? Option_TxOutZ.none() : Option_TxOutZ.some(changeTxOut.TxOut())));
        }
        catch (Exception)
        {
            return Result_CoinSelectionNoneZ.err();
        }
    }

    public Result_TransactionNoneZ sign_psbt(byte[] psbtBytes)
    {
       var signedPsbt =  onChainWalletManager.SignTransaction(psbtBytes).GetAwaiter().GetResult();
       return signedPsbt is null
           ? Result_TransactionNoneZ.err()
           : Result_TransactionNoneZ.ok(signedPsbt.ExtractTransaction().ToBytes());
    }
}
