using System.Net;
using System.Text.Json.Serialization;
using BTCPayApp.Core.JsonConverters;

namespace BTCPayApp.Core.Data;

public record PeerInfo
{
    [JsonConverter(typeof(EndPointJsonConverter))]
    public EndPoint? Endpoint { get; set; }
    public bool Persistent { get; set; }
    public bool Trusted { get; set; }
    public string? Label { get; set; }
}