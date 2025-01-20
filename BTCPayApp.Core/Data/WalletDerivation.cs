namespace BTCPayApp.Core.Data;

public class WalletDerivation
{
    public const string NativeSegwit = "segwit";
    public const string LightningScripts = "lightningScripts";
    // public const string SpendableOutputs = "spendableOutputs";

    public required string Name { get; set; }
    public string? Identifier { get; set; }
    public string? Descriptor { get; set; }

    // TODO: this is useful when restoring, to tell NBX to generate addresses up to this to prevent address reuse.
    public int? LastKnownIndex{ get; set; }
}
