using System.Text.Json.Serialization;

namespace BTCPayApp.Core.LSP.JIT;

public class FlowFeeResponse
{
    [JsonPropertyName("fee_amount_msat")] public long Amount { get; set; }
    [JsonPropertyName("id")] public required string Id { get; set; }
}