using System.Text.Json.Serialization;

namespace BTCPayApp.Core.LSP.JIT;

public class FlowProposalRequest
{
    [JsonPropertyName("bolt11")] public required string Bolt11 { get; set; }

    [JsonPropertyName("host")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Host { get; set; }


    [JsonPropertyName("port")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Port { get; set; }

    [JsonPropertyName("fee_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FeeId { get; set; }
}