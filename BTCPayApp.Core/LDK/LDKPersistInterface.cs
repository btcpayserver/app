using BTCPayApp.Core.Attempt2;
using org.ldk.enums;
using org.ldk.structs;
using OutPoint = org.ldk.structs.OutPoint;
using Script = NBitcoin.Script;

namespace BTCPayApp.Core.LDK;

public class LDKPersistInterface : PersistInterface
{
    private readonly LDKNode _node;
    public LDKPersistInterface(LDKNode node)
    {
        _node = node;
    }

    public ChannelMonitorUpdateStatus persist_new_channel(OutPoint channel_funding_outpoint, ChannelMonitor data,
        MonitorUpdateId update_id)
    {
        //TODO: store update id  so that we can do this async
        
        try
        {
            var outs = data.get_outputs_to_watch().SelectMany(zzzz => zzzz.get_b().Select(zz => Script.FromBytesUnsafe(zz.get_b()))).ToArray();
            
            _node.TrackScripts(outs).GetAwaiter().GetResult();
            ChannelId.v1_from_funding_outpoint(channel_funding_outpoint);
            var id = Convert.ToHexString(ChannelId.v1_from_funding_outpoint(channel_funding_outpoint).get_a());
            
            _node.UpdateChannel(id, data.write()).GetAwaiter().GetResult();
            return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_Completed;
            
        }
        catch (Exception e)
        {
            return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_UnrecoverableError;
        }

    }


    public ChannelMonitorUpdateStatus update_persisted_channel(OutPoint channel_funding_outpoint, ChannelMonitorUpdate update,
        ChannelMonitor data, MonitorUpdateId update_id)
    {
        _node.UpdateChannel(Convert.ToHexString(ChannelId.v1_from_funding_outpoint(channel_funding_outpoint).get_a()), data.write()).GetAwaiter().GetResult();
        return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_Completed;
    }

    public void archive_persisted_channel(OutPoint channel_funding_outpoint)
    {
        throw new NotImplementedException();
    }
}