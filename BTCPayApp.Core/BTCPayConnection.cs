using BTCPayApp.CommonServer;
using BTCPayServer.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TypedSignalR.Client;

namespace BTCPayApp.Core;

public class BTCPayAppServerClient : IBTCPayAppServerClient
{
    private readonly ILogger<BTCPayAppServerClient> _logger;

    public BTCPayAppServerClient(ILogger<BTCPayAppServerClient> logger)
    {
        _logger = logger;
    }

    public Task TransactionDetected(string txid)
    {
        _logger.LogInformation("OnTransactionDetected: {Txid}", txid);
        OnTransactionDetected?.Invoke(this, txid);
        return Task.CompletedTask;
    }

    public Task NewBlock(string block)
    {
        _logger.LogInformation("NewBlock: {block}", block);
        OnNewBlock?.Invoke(this, block);
        return Task.CompletedTask;
    }

    public event EventHandler<string>? OnNewBlock;
    public event EventHandler<string>? OnTransactionDetected;
}

public class BTCPayConnection : IHostedService, IHubConnectionObserver
{
    private readonly BTCPayAppConfigManager _btcPayAppConfigManager;
    private readonly ILogger<BTCPayConnection> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBTCPayAppServerClient _btcPayAppServerClient;
    private IDisposable? _subscription;
    public  IBTCPayAppServerHub? HubProxy { get; private set; }
    public BTCPayServerClient? Client { get; set; }
    public HubConnection? Connection { get; private set; }
    public event EventHandler? ConnectionChanged;
    private HubConnectionState? _lastAdvertisedState = null;

    private void InvokeConnectionChange()
    {
        if (_lastAdvertisedState != Connection?.State)
        {
            _lastAdvertisedState = Connection?.State;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public BTCPayConnection(BTCPayAppConfigManager btcPayAppConfigManager,
        ILogger<BTCPayConnection> logger,
        IHttpClientFactory httpClientFactory,
        IBTCPayAppServerClient btcPayAppServerClient)
    {
        _btcPayAppConfigManager = btcPayAppConfigManager;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _btcPayAppServerClient = btcPayAppServerClient;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _btcPayAppConfigManager.Loaded.Task;
        OnPairConfigUpdated(_btcPayAppConfigManager, _btcPayAppConfigManager.PairConfig);

        _btcPayAppConfigManager.PairConfigUpdated += OnPairConfigUpdated;
        _btcPayAppConfigManager.WalletConfigUpdated += OnWalletConfigUpdated;
        _ = TryStayConnected();
    }

    private async Task TryStayConnected()
    {
        while (true)
        {
            try
            {
                if (Connection is not null && Connection.State == HubConnectionState.Disconnected)
                {
                    _logger.LogInformation("Connecting to BTCPayServer...");
                    var startTsk = Connection.StartAsync();
                    InvokeConnectionChange();
                    await startTsk;
                    InvokeConnectionChange();
                }
                else
                {
                    InvokeConnectionChange();
                    await Task.Delay(5000);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while connecting to BTCPayServer");
                InvokeConnectionChange();
                await Task.Delay(1000);
            }
        }
    }

    private void OnWalletConfigUpdated(object? sender, WalletConfig? e)
    {
        // throw new NotImplementedException();
    }

    private void OnPairConfigUpdated(object? sender, BTCPayPairConfig? e)
    {
        if (e?.PairingInstanceUri is not null && e.PairingResult.Key is not null)
            _ = StartOrReplace();
        else
            _ = Kill();
    }


    private async Task Kill()
    {
        if (Connection is not null)
            await Connection.StopAsync();
        Connection = null;
        Client = null;
        InvokeConnectionChange();
        _subscription?.Dispose();
        HubProxy = null;
    }

    private async Task StartOrReplace()
    {
        await Kill();
        Connection = new HubConnectionBuilder()
            .WithUrl(_btcPayAppConfigManager.PairConfig!.PairingInstanceUri + "/hub/btcpayapp", options =>
            {
                options.Headers.Add(new KeyValuePair<string?, string?>("Authorization",
                    $"token {_btcPayAppConfigManager.PairConfig!.PairingResult.Key}"));
            })
            .WithAutomaticReconnect()
            .Build();
        Client = new BTCPayServerClient(new Uri(_btcPayAppConfigManager.PairConfig!.PairingInstanceUri),
            _btcPayAppConfigManager.PairConfig!.PairingResult.Key, _httpClientFactory.CreateClient("btcpayserver"));

        InvokeConnectionChange();
        _subscription = Connection.Register(_btcPayAppServerClient);
        HubProxy = Connection.CreateHubProxy<IBTCPayAppServerHub>();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _btcPayAppConfigManager.PairConfigUpdated -= OnPairConfigUpdated;
        _btcPayAppConfigManager.WalletConfigUpdated -= OnWalletConfigUpdated;
        return Task.CompletedTask;
    }

    public Task OnClosed(Exception? exception)
    {
        InvokeConnectionChange();
        _logger.LogError(exception, "OnClosed");
        return Task.CompletedTask;
    }

    public Task OnReconnected(string? connectionId)
    {
        InvokeConnectionChange();
        _logger.LogInformation("OnReconnected: {ConnectionId}", connectionId);
        return Task.CompletedTask;
    }

    public Task OnReconnecting(Exception? exception)
    {
        InvokeConnectionChange();
        _logger.LogError(exception, "OnReconnecting");
        return Task.CompletedTask;
    }
}
