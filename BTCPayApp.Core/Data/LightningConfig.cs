using System.Text.Json.Serialization;

namespace BTCPayApp.Core.Data;
public class LightningConfig
{
    public const string Key = "lightningconfig";

    public string Alias { get; set; } = "BTCPay Server";
    public string ScriptDerivationKey { get; set; } = WalletDerivation.NativeSegwit; //when ldk asks for an address, where do we get it from?
    public string LightningDerivationPath { get; set; } = "m/666'";// your lightning node derivation path
    public string Color { get; set; } = "#51B13E";

    public string? JITLSP { get; set; } // Just In Time Lightning Service Provider

    [JsonIgnore]
    public byte[] RGB
    {
        get
        {
            if(string.IsNullOrEmpty(Color)){ return [0,0,0];}

            if (Color.StartsWith("#"))
            {
                var rgBint = Convert.ToInt32(Color.Substring(1), 16);
                var red = (byte)((rgBint >> 16) & 255);
                var green = (byte)((rgBint >> 8) & 255);
                var blue = (byte)(rgBint & 255);
                return [red, green, blue];
            }
            else
            {
                var parts = Color.Split(',');
                if (parts.Length != 3)
                {
                    return [0, 0, 0];
                }
                return [byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2])];
            }
        }
    }
    
    public Dictionary<string, PeerInfo> Peers { get; set; } = new();

    public bool AcceptInboundConnection{ get; set; }
}

public record PeerInfo
{
    public string Endpoint { get; set; }
    public bool Persistent { get; set; }
    public bool Trusted { get; set; }
}