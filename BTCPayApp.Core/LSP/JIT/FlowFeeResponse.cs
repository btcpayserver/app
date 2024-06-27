using Newtonsoft.Json;

namespace BTCPayApp.Core.LSP.JIT;

public class FlowFeeResponse
{
    [JsonProperty("amount_msat")] public long Amount { get; set; }
    [JsonProperty("id")] public required string Id { get; set; }
}