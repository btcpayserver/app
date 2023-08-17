using BTCPayApp.CommonServer;
using NBitcoin;

namespace BTCPayApp.Core;

public class BTCPayPairConfig
{
    public PairSuccessResult? PairingResult { get; set; }
    public string? PairingInstanceUri { get; set; }
}

public class WalletConfig
{
    public string? Mnemonic { get; set; }
    public string? DerivationPath { get; set; }
    public bool StandaloneMode { get; set; } = false;
}

