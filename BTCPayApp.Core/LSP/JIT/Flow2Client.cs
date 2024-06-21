using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
using BTCPayServer.Lightning;
using NBitcoin;
using Newtonsoft.Json;
using org.ldk.structs;
using org.ldk.util;
using LightningPayment = BTCPayApp.CommonServer.Models.LightningPayment;

namespace BTCPayApp.Core.LSP.JIT;

public class FlowInvoiceDetails()
{

    public string FeeId { get; set; }
    public long FeeAmount { get; set; }
    
}

/// <summary>
/// https://docs.voltage.cloud/flow/flow-2.0
/// </summary>
public class VoltageFlow2Jit : IJITService, IScopedHostedService, ILDKEventHandler<Event.Event_ChannelPending>

{
    private readonly HttpClient _httpClient;
    private readonly Network _network;
    private readonly LDKNode _node;
    private readonly ChannelManager _channelManager;

    public static Uri? BaseAddress(Network network)
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
        ChannelManager channelManager)
    {
        var httpClientInstance = httpClientFactory.CreateClient("VoltageFlow2JIT");
        httpClientInstance.BaseAddress = BaseAddress(network);

        _httpClient = httpClientInstance;
        _network = network;
        _node = node;
        _channelManager = channelManager;
    }

    public VoltageFlow2Jit(HttpClient httpClient, Network network)
    {
        if (httpClient.BaseAddress == null)
            throw new ArgumentException(
                "HttpClient must have a base address, use Flow2Client.BaseAddress to get a predefined URI",
                nameof(httpClient));

        _httpClient = httpClient;
        _network = network;
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

    public string ProviderName => "Voltage";
    public async Task<(LightMoney invoiceAmount, LightMoney fee)> CalculateInvoiceAmount(LightMoney expectedAmount)
    {
        var fee = await GetFee(expectedAmount, _node.NodeId);
        return (LightMoney.MilliSatoshis(expectedAmount.MilliSatoshi-fee.Amount), LightMoney.MilliSatoshis(fee.Amount));
    }

    public async Task WrapInvoice(LightningPayment lightningPayment)
    {

        if (lightningPayment.PaymentRequests.Count > 1)
        {
            return;
        }
        if(lightningPayment.AdditionalData?.TryGetValue("flowlsp", out var lsp) is true && lsp.RootElement.Deserialize<FlowInvoiceDetails>() is { } invoiceDetails)
            return;
        
        var lm = new LightMoney(lightningPayment.Value);
        var fee = await GetFee(lm, _node.NodeId);
        
        
        if (lm < fee.Amount)
            throw new InvalidOperationException("Invoice amount is too low to use Voltage LSP");
        
        
        return await GetProposal(invoice, null, fee.Id);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _node.ConfigUpdated += ConfigUpdated;
        _ = Task.Run(async () =>
        {

            await ConfigUpdated(this, await _node.GetConfig());
        }, cancellationToken);
    }

    private FlowInfoResponse? _info;

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

    private bool VerifyInvoice(BOLT11PaymentRequest ourInvoice,
        BOLT11PaymentRequest lspInvoice,
        LightMoney fee)
    {
        if(ourInvoice.PaymentHash != lspInvoice.PaymentHash)
            return false;
        
        var expected_lsp_invoice_amt = our_invoice_amt + lsp_fee_msats;
        
        if (Bolt11Invoice.from_str(ourInvoice.ToString()) is Result_Bolt11InvoiceParseOrSemanticErrorZ.Result_Bolt11InvoiceParseOrSemanticErrorZ_OK
                ourInvoiceResult &&
            Bolt11Invoice.from_str(lspInvoice.ToString()) is Result_Bolt11InvoiceParseOrSemanticErrorZ.Result_Bolt11InvoiceParseOrSemanticErrorZ_OK lspInvoiceResult)
        {
            ourInvoiceResult.res.
            
        }

        return false;

        if lsp_invoice.network() != our_invoice.network() {
            return Some(format!(
                "Received invoice on wrong network: {} != {}",
                lsp_invoice.network(),
                our_invoice.network()
            ));
        }

        if lsp_invoice.payment_hash() != our_invoice.payment_hash() {
            return Some(format!(
                "Received invoice with wrong payment hash: {} != {}",
                lsp_invoice.payment_hash(),
                our_invoice.payment_hash()
            ));
        }

        let invoice_pubkey = lsp_invoice.recover_payee_pub_key();
        if invoice_pubkey != self.pubkey {
            return Some(format!(
                "Received invoice from wrong node: {invoice_pubkey} != {}",
                self.pubkey
            ));
        }

        if lsp_invoice.amount_milli_satoshis().is_none() {
            return Some("Invoice amount is missing".to_string());
        }

        if our_invoice.amount_milli_satoshis().is_none() {
            return Some("Invoice amount is missing".to_string());
        }

        let lsp_invoice_amt = lsp_invoice.amount_milli_satoshis().expect("just checked");
        let our_invoice_amt = our_invoice.amount_milli_satoshis().expect("just checked");

        let expected_lsp_invoice_amt = our_invoice_amt + lsp_fee_msats;

        // verify invoice within 10 sats of our target
        if lsp_invoice_amt.abs_diff(expected_lsp_invoice_amt) > 10_000 {
            return Some(format!(
                "Received invoice with wrong amount: {lsp_invoice_amt} when amount was {expected_lsp_invoice_amt}",
            ));
        }

        None
    }
    
}