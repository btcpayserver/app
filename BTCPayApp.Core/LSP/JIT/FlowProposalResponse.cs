using Newtonsoft.Json;

namespace BTCPayApp.Core.LSP.JIT;

public class FlowProposalResponse
{
    [JsonProperty("jit_bolt11")] public required string WrappedBolt11 { get; set; }
}