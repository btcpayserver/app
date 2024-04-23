using NBitcoin;

namespace nldksample.LSP.Flow;

public interface IBroadcastGateKeeper
{
    bool DontBroadcast(Transaction loadedTx);
}