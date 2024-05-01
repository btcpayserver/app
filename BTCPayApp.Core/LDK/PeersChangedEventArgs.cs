using BTCPayServer.Lightning;

namespace BTCPayApp.Core.LDK;

public class PeersChangedEventArgs : EventArgs
{
    public List<NodeInfo> PeerNodeIds { get; set; }

    public PeersChangedEventArgs(List<NodeInfo> peerNodeIds)
    {
        PeerNodeIds = peerNodeIds;
    }
}