using System.Net;
using System.Text.Json;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
using BTCPayServer.Lightning;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using org.ldk.structs;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BTCPayApp.Core.LSP.JIT;


/// <summary>
/// https://docs.voltage.cloud/flow/flow-2.0
/// </summary>
public class VoltageFlow2Jit : IJITService, IScopedHostedService, ILDKEventHandler<Event.Event_ChannelPending>

{
    private readonly HttpClient _httpClient;
    private readonly Network _network;
    private readonly LDKNode _node;
    private readonly ChannelManager _channelManager;
    private readonly ILogger<VoltageFlow2Jit> _logger;
    

    public virtual Uri? BaseAddress(Network network)
    {
        return network switch
        {
            not null when network == Network.Main => new Uri("https://lsp.voltageapi.com"),
            not null when network == Network.TestNet => new Uri("https://testnet-lsp.voltageapi.com"),
            // not null when network == Network.RegTest => new Uri("https://localhost:5001/jit-lsp"),
            _ => null
        };
    }

    public VoltageFlow2Jit(IHttpClientFactory httpClientFactory, Network network, LDKNode node,
        ChannelManager channelManager, ILogger<VoltageFlow2Jit> logger)
    {
        var httpClientInstance = httpClientFactory.CreateClient("VoltageFlow2JIT");
        httpClientInstance.BaseAddress = BaseAddress(network);
        Active = httpClientInstance.BaseAddress is not null;

        _httpClient = httpClientInstance;
        _network = network;
        _node = node;
        _channelManager = channelManager;
        _logger = logger;
    }

    public async Task<FlowInfoResponse> GetInfo(CancellationToken cancellationToken = default)
    {
        var path = "/api/v1/info";
        var response = await _httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonConvert.DeserializeObject<FlowInfoResponse>(content);
    }

    public async Task<FlowFeeResponse> GetFee(LightMoney amount, PubKey pubkey,
        CancellationToken cancellationToken = default)
    {
        var path = "/api/v1/fee";
        var request = new FlowFeeRequest(amount, pubkey);
        var response = await _httpClient.PostAsync(path, new StringContent(JsonConvert.SerializeObject(request)),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonConvert.DeserializeObject<FlowFeeResponse>(content);
    }

    public async Task<BOLT11PaymentRequest> GetProposal(BOLT11PaymentRequest bolt11PaymentRequest,
        EndPoint? endPoint = null, string? feeId = null, CancellationToken cancellationToken = default)
    {
        var path = "/api/v1/proposal";
        var request = new FlowProposalRequest()
        {
            Bolt11 = bolt11PaymentRequest.ToString(),
            Host = endPoint?.Host(),
            Port = endPoint?.Port(),
            FeeId = feeId,
        };
        var response = await _httpClient
            .PostAsync(path, new StringContent(JsonConvert.SerializeObject(request)), cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonConvert.DeserializeObject<FlowProposalResponse>(content);

        return BOLT11PaymentRequest.Parse(result!.WrappedBolt11, _network);
    }

    public virtual string ProviderName => "Voltage";
    public async Task<JITFeeResponse?> CalculateInvoiceAmount(LightMoney expectedAmount)
    {
        try
        {

            var fee = await GetFee(expectedAmount, _node.NodeId);
            return new JITFeeResponse(expectedAmount, expectedAmount + fee.Amount, fee.Amount, fee.Id, ProviderName);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while calculating invoice amount");
            return null;
        }
    }

    public const string LightningPaymentJITFeeKey = "JITFeeKey";
    public const string LightningPaymentLSPKey = "LSP";
    public const string LightningPaymentOriginalPaymentRequest = "OriginalPaymentRequest";
    public async Task<bool> WrapInvoice(AppLightningPayment lightningPayment, JITFeeResponse? fee)
    {
        if(lightningPayment.AdditionalData?.ContainsKey(LightningPaymentLSPKey) is true)
            return false;
        
        
        fee??= await CalculateInvoiceAmount(new LightMoney(lightningPayment.Value));
        
        if(fee is null)
            return false;
        var invoice = lightningPayment.PaymentRequest;
        
        
        var proposal =  await GetProposal(invoice,null, fee!.FeeIdentifier);
        if(proposal.MinimumAmount != fee.AmountToRequestPayer || proposal.PaymentHash != invoice.PaymentHash)
            return false;
        lightningPayment.PaymentRequest = proposal;
        lightningPayment.AdditionalData ??= new Dictionary<string, JsonElement>();
        lightningPayment.AdditionalData[LightningPaymentOriginalPaymentRequest] = JsonSerializer.SerializeToElement(invoice);
        lightningPayment.AdditionalData[LightningPaymentLSPKey] = JsonSerializer.SerializeToElement(ProviderName);
        lightningPayment.AdditionalData[LightningPaymentJITFeeKey] = JsonSerializer.SerializeToElement(fee);
        return true;
    }

    public bool Active { get; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _node.ConfigUpdated += ConfigUpdated;
        _ = ConfigUpdated(this, await _node.GetConfig()).WithCancellation(cancellationToken);
    }

    private FlowInfoResponse? _info;

    public VoltageFlow2Jit(bool active)
    {
        Active = active;
    }

    private async Task ConfigUpdated(object? sender, LightningConfig e)
    {
        if (e.JITLSP == ProviderName)
        {
            _info ??= await GetInfo();


            var ni = _info.ToNodeInfo();
            var configPeers = await _node.GetConfig();
            var pubkey = new PubKey(_info.PubKey);
            if (configPeers.Peers.TryGetValue(_info.PubKey, out var peer))
            {
                //check if the endpoint matches any of the info ones 
                if(!_info.ConnectionMethods.Any(a => a.ToEndpoint().ToEndpointString().Equals(peer.Endpoint, StringComparison.OrdinalIgnoreCase)))
                {
                    peer = new PeerInfo {Endpoint = _info.ConnectionMethods.First().ToEndpoint().ToEndpointString(), Persistent = true, Trusted = true};
                }else if (peer is {Persistent: true, Trusted: true})
                    return;
                else
                {
                    peer = peer with
                    {
                        Persistent = true,
                        Trusted = true
                    };
                }
            }
            else
            {
                
                peer = new PeerInfo {Endpoint = _info.ConnectionMethods.First().ToEndpoint().ToEndpointString(), Persistent = true, Trusted = true};
            }
            
            _ = _node.Peer(pubkey, peer);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _node.ConfigUpdated -= ConfigUpdated;
    }

    public async Task Handle(Event.Event_ChannelPending @event)
    {
        var nodeId = new PubKey(@event.counterparty_node_id);
        if(nodeId.ToString() == _info?.PubKey)
        {
            var channel = _channelManager
                .list_channels_with_counterparty(@event.counterparty_node_id)
                .FirstOrDefault(a => a.get_channel_id().eq(@event.channel_id));
           if(channel is null)
               return;
           var channelConfig = channel.get_config();
           channelConfig.set_accept_underpaying_htlcs(true);
           _channelManager.update_channel_config(@event.counterparty_node_id, new[] {@event.channel_id}, channelConfig);
        }
    }
    
}

public class OlympusFlow2Jit : VoltageFlow2Jit
{
    public OlympusFlow2Jit(IHttpClientFactory httpClientFactory, Network network, LDKNode node, ChannelManager channelManager, ILogger<VoltageFlow2Jit> logger) : base(httpClientFactory, network, node, channelManager, logger)
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
}