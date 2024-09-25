using BTCPayApp.Core.LDK;
using BTCPayServer.Lightning;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LSP.JIT;

public class OlympusFlow2Jit : VoltageFlow2Jit
{
    public OlympusFlow2Jit(IHttpClientFactory httpClientFactory, Network network, LDKNode node, ChannelManager channelManager, ILogger<VoltageFlow2Jit> logger, 
        LDKOpenChannelRequestEventHandler openChannelRequestEventHandler) : base(httpClientFactory, network, node, channelManager, logger, openChannelRequestEventHandler)
    {
    }

    public override Uri? BaseAddress(Network network)
    {
        return network switch
        {
            not null when network == Network.Main => new Uri("https://0conf.lnolymp.us"),
            not null when network == Network.TestNet => new Uri("https://testnet-0conf.lnolymp.us"),
            // not null when network == Network.RegTest => new Uri("https://localhost:5001/jit-lsp"),
            _ => null
        };
    }

    public override string ProviderName => "Olympus";

    protected override LightMoney NonChannelOpenFee => LightMoney.Satoshis(2);
}