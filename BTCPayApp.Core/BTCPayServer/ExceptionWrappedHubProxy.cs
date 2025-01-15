﻿using BTCPayApp.Core.Helpers;
using BTCPayServer.Client.App;
using BTCPayServer.Lightning;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using TypedSignalR.Client;

namespace BTCPayApp.Core.BTCPayServer;

public class ExceptionWrappedHubProxy : IBTCPayAppHubServer
{
    private readonly IBTCPayAppHubServer _hubProxy;
    private readonly ILogger _logger;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly HubConnection _connection;

    public ExceptionWrappedHubProxy(BTCPayConnectionManager btcPayConnectionManager ,HubConnection connection, ILogger logger)
    {
        _btcPayConnectionManager = btcPayConnectionManager;
        _connection = connection;
        _hubProxy = connection.CreateHubProxy<IBTCPayAppHubServer>();
        _logger = logger;
    }

    
    private async Task<T> Wrap<T>(Func<Task<T>> func)
    {

        
        return await AsyncExtensions.RunInOtherThread(async () =>
        {
            //executes in thread pool.
            try
            {
                return await func();
            }
            catch (InvalidOperationException e)
            {
                _logger.LogError(e, $"Error while calling hub method");
                return default!;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while calling hub method");
                return default!;
                
            }
        }).Unwrap();
    }


    public async Task<bool> DeviceMasterSignal(long deviceIdentifier, bool active)
    {
        return await Wrap(async ()=>  await _hubProxy.DeviceMasterSignal(deviceIdentifier, active));
    }

    public async Task<Dictionary<string, string>> Pair(PairRequest request)
    {
        return await Wrap(async ()=>  await _hubProxy.Pair(request));
    }

    public async Task<AppHandshakeResponse> Handshake(AppHandshake request)
    {
        return await Wrap(async ()=>  await _hubProxy.Handshake(request));
    }

    public async Task<bool> BroadcastTransaction(string tx)
    {
        return await Wrap(async ()=>  await _hubProxy.BroadcastTransaction(tx));
    }

    public async Task<decimal> GetFeeRate(int blockTarget)
    {
        return await Wrap(async ()=>  await _hubProxy.GetFeeRate(blockTarget));
    }

    public async Task<BestBlockResponse> GetBestBlock()
    {
        return await Wrap(async ()=>  await _hubProxy.GetBestBlock());
    }

    public async Task<TxInfoResponse> FetchTxsAndTheirBlockHeads(string identifier, string[] txIds, string[] outpoints)
    {
        return await Wrap(async ()=>  await _hubProxy.FetchTxsAndTheirBlockHeads(identifier, txIds, outpoints));
    }

    public async Task<ScriptResponse> DeriveScript(string identifier)
    {
        return await Wrap(async ()=>  await _hubProxy.DeriveScript(identifier));
    }

    public async Task TrackScripts(string identifier, string[] scripts)
    {
        await Wrap(()=>   Task.FromResult(_hubProxy.TrackScripts(identifier, scripts)));
    }

    public async Task<string> UpdatePsbt(string[] identifiers, string psbt)
    {
        return await Wrap(async ()=>  await _hubProxy.UpdatePsbt(identifiers, psbt));
    }

    public async Task<Dictionary<string, CoinResponse[]>> GetUTXOs(string[] identifiers)
    {
        return await Wrap(async ()=>  await _hubProxy.GetUTXOs(identifiers));
    }

    public async Task<Dictionary<string, TxResp[]>> GetTransactions(string[] identifiers)
    {
        return await Wrap(async ()=>  await _hubProxy.GetTransactions(identifiers));
    }

    public async Task SendInvoiceUpdate(LightningInvoice lightningInvoice)
    {
        await Wrap(() => Task.FromResult(_hubProxy.SendInvoiceUpdate(lightningInvoice)));
    }

    public async Task<long?> GetCurrentMaster()
    {
        return await Wrap(async ()=>  await _hubProxy.GetCurrentMaster());
    }
}