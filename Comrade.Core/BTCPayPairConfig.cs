using NBitcoin;

namespace Comrade.Core;

public class BTCPayPairConfig
{
    public PairSuccessResult? PairingResult { get; set; }

    public string? PairingInstanceUri { get; set; }

}

public class WalletConfig
{
    public string? Seed { get; set; }
    
}