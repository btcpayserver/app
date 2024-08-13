using System.Text.Json.Serialization;
using BTCPayServer.Lightning;
using NBitcoin;

namespace BTCPayApp.Core.LSP.JIT;

public class FlowFeeRequest
{
    public FlowFeeRequest()
    {
    }

    public FlowFeeRequest(LightMoney amount, PubKey pubkey)
    {
        Amount = amount.MilliSatoshi;
        PubKey = pubkey.ToHex();
    }

    [JsonPropertyName("amount_msat")] public long Amount { get; set; }
    [JsonPropertyName("pubkey")] public string PubKey { get; set; }
}