﻿using BTCPayApp.CommonServer;
using NBitcoin;

namespace BTCPayApp.Core;

public class BTCPayPairConfig
{
    public PairSuccessResult? PairingResult { get; set; }

    public string? PairingInstanceUri { get; set; }

}

public class WalletConfig
{
    public string? Seed { get; set; }
    
}