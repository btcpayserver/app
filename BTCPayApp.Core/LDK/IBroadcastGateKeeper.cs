using NBitcoin;

namespace BTCPayApp.Core.LDK;

/// <summary>
/// LDK will occassionally need to broadcast transactions. This interface allows the implementer to gatekeep these txs it wishes to broadcast.
/// Can be useful when you may want to do a trusted agreement with a peer and allow a 0 conf channel without even broadcasting the tx.
/// </summary>
public interface IBroadcastGateKeeper
{
    bool DontBroadcast(Transaction loadedTx);
}