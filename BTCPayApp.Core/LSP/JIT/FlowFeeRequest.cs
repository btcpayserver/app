using BTCPayServer.Lightning;
using NBitcoin;
using Newtonsoft.Json;

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

    [JsonProperty("amount_msat")] public long Amount { get; set; }
    [JsonProperty("pubkey")] public string PubKey { get; set; }
}