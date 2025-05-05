using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKPersister(LDKNode ldkNode) : PersisterInterface
{
    public Result_NoneIOErrorZ persist_manager(ChannelManager channelManager)
    {
        ldkNode.UpdateChannelManager(channelManager).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }

    public Result_NoneIOErrorZ persist_graph(NetworkGraph networkGraph)
    {
        ldkNode.UpdateNetworkGraph(networkGraph).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }

    public Result_NoneIOErrorZ persist_scorer(WriteableScore scorer)
    {
        ldkNode.UpdateScore(scorer).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }
}
