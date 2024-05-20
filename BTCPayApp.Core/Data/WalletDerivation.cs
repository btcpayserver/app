namespace BTCPayApp.Core.Data;

public class WalletDerivation
{
    public string Identifier { get; set; }
    public string Name { get; set; }
    public string? Descriptor { get; set; }

    public const string NativeSegwit = "segwit";
    public const string LightningScripts = "lightningScripts";
    // public const string SpendableOutputs = "spendableOutputs";
}