using NBitcoin;
using org.ldk.structs;
using UInt128 = org.ldk.util.UInt128;

namespace nldksample.LDK;

public class LDKOpenChannelRequestEventHandler: ILDKEventHandler<Event.Event_OpenChannelRequest>
{
    private readonly ChannelManager _channelManager;

    public LDKOpenChannelRequestEventHandler(ChannelManager channelManager)
    {
        _channelManager = channelManager;
    }
    public async Task Handle(Event.Event_OpenChannelRequest eventOpenChannelRequest)
    {
        if (eventOpenChannelRequest.channel_type.supports_zero_conf())
        {
            _channelManager.accept_inbound_channel_from_trusted_peer_0conf(
                eventOpenChannelRequest.temporary_channel_id,
                eventOpenChannelRequest.counterparty_node_id,
                new UInt128(RandomUtils.GetBytes(16))
            );
        }
        else
        {
            _channelManager.accept_inbound_channel(
                eventOpenChannelRequest.temporary_channel_id,
                eventOpenChannelRequest.counterparty_node_id,
                new UInt128(RandomUtils.GetBytes(16)));
        }
        
    }
}