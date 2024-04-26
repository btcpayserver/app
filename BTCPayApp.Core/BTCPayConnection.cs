using System.Net.Http.Headers;
using BTCPayApp.CommonServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.UI.Auth;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using TypedSignalR.Client;

namespace BTCPayApp.Core;

public class BTCPayAppServerClient : IBTCPayAppHubClient
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
    private readonly IAccountManager _accountManager;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly IConfigProvider _configProvider;
    private readonly ILogger<BTCPayConnection> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBTCPayAppHubClient _btcPayAppServerClient;
    private IDisposable? _subscription;

    public IBTCPayAppHubServer? HubProxy { get; private set; }

    // public BTCPayServerClient? Client { get; set; }
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

    public BTCPayConnection(
        IAccountManager accountManager,
        AuthenticationStateProvider authStateProvider,
        IConfigProvider configProvider,
        ILogger<BTCPayConnection> logger,
        IHttpClientFactory httpClientFactory,
        IBTCPayAppHubClient btcPayAppServerClient)
    {
        _accountManager = accountManager;
        _authStateProvider = authStateProvider;
        _configProvider = configProvider;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _btcPayAppServerClient = btcPayAppServerClient;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _authStateProvider.AuthenticationStateChanged += AuthStateProviderOnAuthenticationStateChanged;
        _ = TryStayConnected();
    }

    private void AuthStateProviderOnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        task.ContinueWith(async task1 =>
        {
            await task1;
            var authenticated = await _accountManager.CheckAuthenticated();
            if (!authenticated)
                await Kill();
            else
            {
                await StartOrReplace();
            }
        });
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


    private async Task Kill()
    {
        if (Connection is not null)
            await Connection.StopAsync();
        Connection = null;
        InvokeConnectionChange();
        _subscription?.Dispose();
        HubProxy = null;
    }

    private async Task StartOrReplace()
    {
        await Kill();
        var account = _accountManager.GetAccount();
        if (account is null)
            return;
        Connection = new HubConnectionBuilder()
            .WithUrl(account.BaseUri + "/hub/btcpayapp", options =>
            {
                options.Headers.Add(new KeyValuePair<string?, string?>("Authorization",
                    new AuthenticationHeaderValue("Bearer", account.AccessToken).ToString()));
            })
            .WithAutomaticReconnect()
            .Build();

        InvokeConnectionChange();
        _subscription = Connection.Register(_btcPayAppServerClient);
        HubProxy = Connection.CreateHubProxy<IBTCPayAppHubServer>();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _authStateProvider.AuthenticationStateChanged -= AuthStateProviderOnAuthenticationStateChanged;
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
   
