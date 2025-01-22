using System.Net;
using System.Text.Json.Serialization;
using BTCPayApp.Core.Helpers;
using BTCPayServer.Lightning;
using NBitcoin;

namespace BTCPayApp.Core.LSP.JIT;

public abstract class FlowInfoResponse
{
    [JsonPropertyName("connection_methods")]
    public ConnectionMethod[] ConnectionMethods { get; set; } = [];
    [JsonPropertyName("pubkey")]
    public required string PubKey { get; set; }

    public NodeInfo[] ToNodeInfo()
    {
        var pubkey = new PubKey(PubKey);
        return ConnectionMethods.Select(method => new NodeInfo(pubkey, method.Address, method.Port)).ToArray();
    }

    public abstract class ConnectionMethod
    {
        [JsonPropertyName("address")]
        public required string Address { get; set; }
        [JsonPropertyName("port")]
        public required int Port { get; set; }
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        public EndPoint? ToEndpoint()
        {
            return EndPointParser.TryParse($"{Address}:{Port}", 9735, out var endpoint) ? endpoint : null;
        }
    }
}
