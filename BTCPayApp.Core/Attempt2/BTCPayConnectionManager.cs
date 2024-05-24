using System.Net.Http.Headers;
using BTCPayApp.CommonServer;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Helpers;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using TypedSignalR.Client;

namespace BTCPayApp.Core.Attempt2;

public class BTCPayConnectionManager : IHostedService, IHubConnectionObserver
{
    private readonly IAccountManager _accountManager;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<BTCPayConnectionManager> _logger;
    private readonly BTCPayAppServerClient _btcPayAppServerClient;
    private readonly IBTCPayAppHubClient _btcPayAppServerClientInterface;
    private IDisposable? _subscription;

    public IBTCPayAppHubServer? HubProxy { get; private set; }
    public HubConnection? Connection { get; private set; }
    public Network? ReportedNetwork { get; private set; }

    public event AsyncEventHandler<HubConnectionState>? ConnectionChanged;
    private HubConnectionState _lastAdvertisedState = HubConnectionState.Disconnected;

    private void InvokeConnectionChange()
    {
        if (_lastAdvertisedState != Connection?.State)
        {
            _lastAdvertisedState = Connection?.State ?? HubConnectionState.Disconnected;
            ConnectionChanged?.Invoke(this, _lastAdvertisedState);
        }
    }

    public BTCPayConnectionManager(
        IAccountManager accountManager,
        AuthenticationStateProvider authStateProvider,
        ILogger<BTCPayConnectionManager> logger,
        BTCPayAppServerClient btcPayAppServerClient,
        IBTCPayAppHubClient btcPayAppServerClientInterface)
    {
        _accountManager = accountManager;
        _authStateProvider = authStateProvider;
        _logger = logger;
        _btcPayAppServerClient = btcPayAppServerClient;
        _btcPayAppServerClientInterface = btcPayAppServerClientInterface;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _authStateProvider.AuthenticationStateChanged += AuthStateProviderOnAuthenticationStateChanged;
        _btcPayAppServerClient.OnNotifyNetwork += BtcPayAppServerClientOnOnNotifyNetwork;
        _btcPayAppServerClient.OnServerNodeInfo += BtcPayAppServerClientOnOnServerNodeInfo;
        await StartOrReplace();
        _ = TryStayConnected();
    }

    private  async Task BtcPayAppServerClientOnOnServerNodeInfo(object? sender, string e)
    {
        ReportedNodeInfo = e;

    }

    public string ReportedNodeInfo { get; set; }

    private async Task BtcPayAppServerClientOnOnNotifyNetwork(object? sender, string e)
    {
        ReportedNetwork = Network.GetNetwork(e);
    }

    private async void AuthStateProviderOnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        try
        {
            await task;
            var authenticated = await _accountManager.CheckAuthenticated();
            if (!authenticated)
                await Kill();
            else
            {
                await StartOrReplace();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while handling authentication state change");
        }
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
            .AddNewtonsoftJsonProtocol()
            .WithUrl(new Uri(new Uri(account.BaseUri), "hub/btcpayapp").ToString(), options =>
            {

                options.AccessTokenProvider = () => Task.FromResult(_accountManager.GetAccount()?.AccessToken);
            })
            .WithAutomaticReconnect()
            .Build();

        InvokeConnectionChange();
        _subscription = Connection.Register(_btcPayAppServerClientInterface);
        HubProxy = Connection.CreateHubProxy<IBTCPayAppHubServer>();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _authStateProvider.AuthenticationStateChanged -= AuthStateProviderOnAuthenticationStateChanged;
        _btcPayAppServerClient.OnNotifyNetwork += BtcPayAppServerClientOnOnNotifyNetwork;
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
