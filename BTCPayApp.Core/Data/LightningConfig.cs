using System.Text.Json.Serialization;

namespace BTCPayApp.Core.Data;
public class LightningConfig
{
    public const string Key = "lightningconfig";

    public string Alias { get; set; } = "BTCPay Server";
    public string ScriptDerivationKey { get; set; } = WalletDerivation.NativeSegwit;
    public string Color { get; set; } = "#51B13E";

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
    
}