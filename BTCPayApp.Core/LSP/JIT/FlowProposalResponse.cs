
using System.Text.Json.Serialization;

namespace BTCPayApp.Core.LSP.JIT;

public class FlowProposalResponse
{
    [JsonPropertyName("jit_bolt11")] public required string WrappedBolt11 { get; set; }
}