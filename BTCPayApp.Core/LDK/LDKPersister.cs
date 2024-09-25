using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKPersister : PersisterInterface
{
    private readonly LDKNode _ldkNode;

    public LDKPersister(LDKNode ldkNode)
    {
        _ldkNode = ldkNode;
    }

    public Result_NoneIOErrorZ persist_manager(ChannelManager channel_manager)
    {
        _ldkNode.UpdateChannelManager(channel_manager).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }

    public Result_NoneIOErrorZ persist_graph(NetworkGraph network_graph)
    {
        _ldkNode.UpdateNetworkGraph(network_graph).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }

    public Result_NoneIOErrorZ persist_scorer(WriteableScore scorer)
    {
        _ldkNode.UpdateScore(scorer).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }
}