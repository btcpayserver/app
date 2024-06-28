namespace BTCPayApp.Core.Data;

public record PeerInfo
{
    public string Endpoint { get; set; }
    public bool Persistent { get; set; }
    public bool Trusted { get; set; }
}