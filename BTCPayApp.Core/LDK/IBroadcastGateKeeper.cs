using NBitcoin;

namespace BTCPayApp.Core.LDK;

public interface IBroadcastGateKeeper
{
    bool DontBroadcast(Transaction loadedTx);
}