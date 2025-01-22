using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
using BTCPayServer.Lightning;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.structs;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BTCPayApp.Core.LSP.JIT;

/// <summary>
/// https://docs.voltage.cloud/flow/flow-2.0
/// </summary>
public class VoltageFlow2Jit : IJITService, IScopedHostedService, ILDKEventHandler<Event.Event_ChannelPending>
{
    private const string LightningPaymentOriginalPaymentRequest = "OriginalPaymentRequest";
    private const string LightningPaymentJITFeeKey = "JITFeeKey";
    public const string LightningPaymentLSPKey = "LSP";

    private readonly HttpClient _httpClient;
    private readonly Network _network;
    private readonly LDKNode _node;
    private readonly ChannelManager _channelManager;
    private readonly ILogger<VoltageFlow2Jit> _logger;
    private readonly LDKOpenChannelRequestEventHandler _openChannelRequestEventHandler;
    private CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<long, Event.Event_OpenChannelRequest> _acceptedChannels = new();
    public bool Active { get; }

    public virtual string ProviderName => "Voltage";
    protected virtual LightMoney NonChannelOpenFee => LightMoney.Zero;

    private FlowInfoResponse? _info;

    public VoltageFlow2Jit(bool active)
    {
        Active = active;
    }

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    protected virtual Uri? BaseAddress(Network network)
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
        ChannelManager channelManager, ILogger<VoltageFlow2Jit> logger,
        LDKOpenChannelRequestEventHandler openChannelRequestEventHandler)
    {
        var httpClientInstance = httpClientFactory.CreateClient("VoltageFlow2JIT");
        httpClientInstance.BaseAddress = BaseAddress(network);
        Active = httpClientInstance.BaseAddress is not null;

        _httpClient = httpClientInstance;
        _network = network;
        _node = node;
        _channelManager = channelManager;
        _logger = logger;
        _openChannelRequestEventHandler = openChannelRequestEventHandler;
    }

    private async Task<FlowInfoResponse> GetInfo(CancellationToken cancellationToken = default)
    {
        var path = "/api/v1/info";
        var response = await _httpClient.GetAsync(path, cancellationToken);
        try
        {
            response.EnsureSuccessStatusCode();
            var res = await response.Content.ReadFromJsonAsync<FlowInfoResponse>(cancellationToken);
            return res!;
        }
        catch (HttpRequestException e)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(e.HttpRequestError, error, e, e.StatusCode);
        }
    }

    private async Task<FlowFeeResponse> GetFee(LightMoney amount, PubKey pubkey,
        CancellationToken cancellationToken = default)
    {
        var path = "/api/v1/fee";
        var request = new FlowFeeRequest(amount, pubkey);
        var response = await _httpClient.PostAsJsonAsync(path, request, cancellationToken);
        try
        {
            response.EnsureSuccessStatusCode();
            var res = await response.Content.ReadFromJsonAsync<FlowFeeResponse>(cancellationToken);
            return res!;
        }
        catch (HttpRequestException e)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(e.HttpRequestError, error, e, e.StatusCode);
        }
    }

    private async Task<BOLT11PaymentRequest> GetProposal(BOLT11PaymentRequest bolt11PaymentRequest,
        EndPoint? endPoint = null, string? feeId = null, CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/proposal";
        var request = new FlowProposalRequest
        {
            Bolt11 = bolt11PaymentRequest.ToString(),
            Host = endPoint?.Host(),
            Port = endPoint?.Port(),
            FeeId = feeId,
        };

        var response = await _httpClient.PostAsJsonAsync(path, request, cancellationToken);
        try
        {
            response.EnsureSuccessStatusCode();
            var rawResponse = await response.Content.ReadFromJsonAsync<FlowProposalResponse>(cancellationToken);


            return BOLT11PaymentRequest.Parse(rawResponse!.WrappedBolt11, _network);
        }
        catch (HttpRequestException e)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(e.HttpRequestError, error, e, e.StatusCode);
        }
    }

    public async Task<JITFeeResponse?> CalculateInvoiceAmount(LightMoney expectedAmount, CancellationToken cancellationToken = default)
    {
        try
        {
            var fee = await GetFee(expectedAmount, _node.NodeId, cancellationToken);
            var amtToGenerate = expectedAmount - fee.Amount;
            return amtToGenerate.MilliSatoshi <= 0
                ? null
                : new JITFeeResponse(expectedAmount, amtToGenerate, fee.Amount, fee.Id, ProviderName);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while calculating invoice amount");
            return null;
        }
    }

    public async Task<bool> WrapInvoice(AppLightningPayment lightningPayment, JITFeeResponse? fee, CancellationToken cancellationToken = default)
    {
        try
        {
            if (lightningPayment.AdditionalData?.ContainsKey(LightningPaymentLSPKey) is true)
                return false;

            fee ??= await CalculateInvoiceAmount(new LightMoney(lightningPayment.Value), cancellationToken);
            if (fee is null)
                return false;

            var invoice = lightningPayment.PaymentRequest!;
            var proposal = await GetProposal(invoice, null, fee!.FeeIdentifier, cancellationToken);
            if (proposal.MinimumAmount != fee.AmountToRequestPayer || proposal.PaymentHash != invoice.PaymentHash)
                return false;
            lightningPayment.PaymentRequest = proposal;
            lightningPayment.AdditionalData ??= new Dictionary<string, JsonElement>();
            lightningPayment.AdditionalData[LightningPaymentOriginalPaymentRequest] =
                JsonSerializer.SerializeToElement(invoice.ToString());
            lightningPayment.AdditionalData[LightningPaymentLSPKey] = JsonSerializer.SerializeToElement(ProviderName);
            lightningPayment.AdditionalData[LightningPaymentJITFeeKey] = JsonSerializer.SerializeToElement(fee);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while wrapping invoice");
        }
        return false;
    }

    public virtual Task<bool> IsAcceptable(AppLightningPayment lightningPayment,
        Event.Event_PaymentClaimable paymentClaimable, CancellationToken cancellationToken = default)
    {
        if (!lightningPayment.AdditionalData.TryGetValue(LightningPaymentLSPKey, out var lsp) || lsp.GetString() != ProviderName)
            return Task.FromResult(false);

        if (!lightningPayment.AdditionalData.TryGetValue(LightningPaymentJITFeeKey, out var feeRaw) || feeRaw.Deserialize<JITFeeResponse>() is not { } fee)
            return Task.FromResult(false);

        if (_acceptedChannels.TryRemove(paymentClaimable.via_channel_id.hash(), out _) && paymentClaimable.counterparty_skimmed_fee_msat == fee.LSPFee.MilliSatoshi)
            return Task.FromResult(true);

        return Task.FromResult(paymentClaimable.counterparty_skimmed_fee_msat == NonChannelOpenFee.MilliSatoshi || paymentClaimable.amount_msat ==  (lightningPayment.Value - NonChannelOpenFee ));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _node.ConfigUpdated += ConfigUpdated;
        _openChannelRequestEventHandler.AcceptedChannel += AcceptedChannel;
        _ = ConfigUpdated(this, await _node.GetConfig()).WithCancellation(_cts.Token);
        _ = Task.Run(async () =>
        {
            while (_cts.Token.IsCancellationRequested == false)
            {
                await ConfigUpdated(this, await _node.GetConfig()).WithCancellation(_cts.Token);
                await Task.Delay(10000, _cts.Token);
            }
        }, _cts.Token);
    }

    private Task AcceptedChannel(object? sender, Event.Event_OpenChannelRequest e)
    {
        if (!string.IsNullOrEmpty(_info?.PubKey) && new PubKey(_info.PubKey) == new PubKey(e.counterparty_node_id))
        {
            _acceptedChannels.TryAdd(e.temporary_channel_id.hash(), e);
        }
        return Task.CompletedTask;
    }

    private async Task ConfigUpdated(object? sender, LightningConfig e)
    {
        try
        {
            await _semaphore.WaitAsync();
            if (e.JITLSP == ProviderName)
            {
                _info = await GetInfo();

                var configPeers = await _node.GetConfig();
                var pubkey = new PubKey(_info.PubKey);
                if (configPeers.Peers.TryGetValue(_info.PubKey, out var peer))
                {
                    //check if the endpoint matches any of the info ones
                    if (!_info.ConnectionMethods.Any(a =>
                            a.ToEndpoint().ToEndpointString().Equals(peer.Endpoint.ToEndpointString(), StringComparison.OrdinalIgnoreCase)))
                    {
                        peer = new PeerInfo
                        {
                            Label = ProviderName,
                            Endpoint = _info.ConnectionMethods.First().ToEndpoint(), Persistent = true,
                            Trusted = true
                        };
                    }
                    else if (peer is {Persistent: true, Trusted: true})
                        return;
                    else
                    {
                        peer = peer with
                        {
                            Label = ProviderName,
                            Persistent = true,
                            Trusted = true
                        };
                    }
                }
                else
                {
                    peer = new PeerInfo
                    {
                        Label = ProviderName,
                        Endpoint = _info.ConnectionMethods.First().ToEndpoint(),
                        Persistent = true,
                        Trusted = true
                    };
                }

                _ = _node.Peer(pubkey, peer);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _node.ConfigUpdated -= ConfigUpdated;
        _openChannelRequestEventHandler.AcceptedChannel -= AcceptedChannel;
        await _cts.CancelAsync();
    }

    public Task Handle(Event.Event_ChannelPending @event)
    {
        var nodeId = new PubKey(@event.counterparty_node_id);
        if (nodeId.ToString() == _info?.PubKey)
        {
            var channel = _channelManager
                .list_channels_with_counterparty(@event.counterparty_node_id)
                .FirstOrDefault(a => a.get_channel_id().eq(@event.channel_id));
            if (channel is null)
                return Task.CompletedTask;
            var channelConfig = channel.get_config();
            channelConfig.set_accept_underpaying_htlcs(true);
            _channelManager.update_channel_config(@event.counterparty_node_id, [@event.channel_id],
                channelConfig);
        }
        return Task.CompletedTask;
    }
}
