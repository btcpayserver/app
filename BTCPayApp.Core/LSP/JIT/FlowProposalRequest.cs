using Newtonsoft.Json;

namespace BTCPayApp.Core.LSP.JIT;

public class FlowProposalRequest
{
    [JsonProperty("bolt11")] public required string Bolt11 { get; set; }

    [JsonProperty("host", NullValueHandling = NullValueHandling.Ignore)]
    public string? Host { get; set; }


    [JsonProperty("port", NullValueHandling = NullValueHandling.Ignore)]
    public int? Port { get; set; }

    [JsonProperty("fee_id", NullValueHandling = NullValueHandling.Ignore)]
    public string? FeeId { get; set; }
}