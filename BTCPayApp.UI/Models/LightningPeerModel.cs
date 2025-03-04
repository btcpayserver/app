using BTCPayApp.Core.Data;

namespace BTCPayApp.UI.Models;

public class LightningPeerModel
{
    public string NodeId { get; set; } = null!;
    public string? Socket { get; set; }
    public bool Connected { get; set; }
    public bool Remembered { get; set; }
    public PeerInfo? Info { get; set; }
}
