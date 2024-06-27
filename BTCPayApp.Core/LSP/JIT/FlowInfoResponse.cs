using System.Net;
using BTCPayApp.Core.Helpers;
using BTCPayServer.Lightning;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayApp.Core.LSP.JIT;

public class FlowInfoResponse
{
    [JsonProperty("connection_methods")] public ConnectionMethod[] ConnectionMethods { get; set; }
    [JsonProperty("pubkey")] public required string PubKey { get; set; }

    public NodeInfo[] ToNodeInfo()
    {
        var pubkey = new PubKey(PubKey);
        return ConnectionMethods.Select(method => new NodeInfo(pubkey, method.Address, method.Port)).ToArray();
    }

    public class ConnectionMethod
    {
        [JsonProperty("address")] public string Address { get; set; }
        [JsonProperty("port")] public int Port { get; set; }
        [JsonProperty("type")] public string Type { get; set; }

        public EndPoint? ToEndpoint()
        {
            return EndPointParser.TryParse($"{Address}:{Port}", 9735, out var endpoint) ? endpoint : null;
        }
    }
}