﻿using System.Text.Json.Serialization;
using BTCPayApp.Core.JsonConverters;
using NBitcoin;

namespace BTCPayApp.Core.Data;

public class WalletConfig
{
    public const string Key = "walletconfig";

    public required string Mnemonic { get; set; }
    public required string Network { get; set; }

    //key is the identifier of the tracker, value is a sub wallet format.
    //for example, we will track native segwit wallet, the descriptor will be wpkh([fingerprint/84'/0'/0']xpub/0/*)
    // or for LN specifics, the descriptor is null, and we track non deterministic scripts
    public Dictionary<string, WalletDerivation> Derivations { get; set; } = new();
    [JsonIgnore]
    public string Fingerprint => new Mnemonic(Mnemonic).DeriveExtKey().GetPublicKey().GetHDFingerPrint().ToString();
    [JsonIgnore]
    public Network? NBitcoinNetwork => NBitcoin.Network.GetNetwork(Network);

    public required BlockSnapshot Birthday { get; set; }

    public required CoinSnapshot CoinSnapshot { get; set; }

}

public class CoinSnapshot
{
    public required BlockSnapshot BlockSnapshot { get; set; }
    public required Dictionary<string, SavedCoin[]> Coins { get; set; }

}

public class SavedCoin
{
    [JsonConverter(typeof(BitcoinSerializableJsonConverterFactory))]
    public required OutPoint Outpoint { get; set; }
    [JsonConverter(typeof(KeyPathJsonConverter))]
    public KeyPath? Path { get; set; }
}

public class BlockSnapshot
{
    public required uint BlockHeight { get; set; }
    [JsonConverter(typeof(UInt256JsonConverter))]
    public required uint256 BlockHash { get; set; }
}
