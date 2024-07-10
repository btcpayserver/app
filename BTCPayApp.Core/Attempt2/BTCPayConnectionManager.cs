using System.Net;
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

    public string ReportedNodeInfo { get; set; }

    public event AsyncEventHandler<(HubConnectionState Old, HubConnectionState New)>? ConnectionChanged;
    private HubConnectionState _connectionState = HubConnectionState.Disconnected;

    public HubConnectionState ConnectionState
    {
        get => Connection?.State ?? HubConnectionState.Disconnected;
        private set
        {
            if (_connectionState == value)
                return;
            var old = _connectionState;
            _connectionState = value;
            _logger.LogInformation("Connection state changed: {State}", _connectionState);
            ConnectionChanged?.Invoke(this, (old, _connectionState));
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
        _authStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _btcPayAppServerClient.OnNotifyNetwork += OnNotifyNetwork;
        _btcPayAppServerClient.OnNotifyServerEvent += OnNotifyServerEvent;
        _btcPayAppServerClient.OnServerNodeInfo += OnServerNodeInfo;
        await StartOrReplace();
        _ = TryStayConnected();
    }

    private async Task OnServerNodeInfo(object? sender, string e)
    {
        ReportedNodeInfo = e;
    }

    private async Task OnNotifyServerEvent(object? sender, IServerEvent serverEvent)
    {
        _logger.LogInformation("OnNotifyServerEvent: {ServerEventType}", serverEvent.Type);
    }

    private async Task OnNotifyNetwork(object? sender, string e)
    {
        ReportedNetwork = Network.GetNetwork(e);
    }

    private async void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        try
        {
            await task;
            var authenticated = await _accountManager.CheckAuthenticated();
            if (!authenticated)
                await Kill();
            else
                await StartOrReplace();
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
                if (Connection is not null && ConnectionState == HubConnectionState.Disconnected)
                {
                    await Connection.StartAsync();
                    ConnectionState = HubConnectionState.Connected;
                }
                else
                {
                    await Task.Delay(5000);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized)
            {
                var result = await _accountManager.RefreshAccess();
                if (result.Succeeded)
                    await StartOrReplace();
                else
                    await Kill();
                await Task.Delay(1000);
            }
            catch (Exception e)
            {
                await Task.Delay(1000);
            }
        }
    }

    private async Task Kill()
    {
        if (Connection is not null)
            await Connection.StopAsync();
        Connection = null;
        ConnectionState = HubConnectionState.Disconnected;
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
            .AddNewtonsoftJsonProtocol(options =>
            {
                NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(options.PayloadSerializerSettings);
            })
            .WithUrl(new Uri(new Uri(account.BaseUri), "hub/btcpayapp").ToString(), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(_accountManager.GetAccount()?.AccessToken);
            })
            .WithAutomaticReconnect()
            .Build();

        _subscription = Connection.Register(_btcPayAppServerClientInterface);
        HubProxy = Connection.CreateHubProxy<IBTCPayAppHubServer>();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _authStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        _btcPayAppServerClient.OnNotifyNetwork += OnNotifyNetwork;
        return Task.CompletedTask;
    }

    public Task OnClosed(Exception? exception)
    {
        _logger.LogError(exception, "Hub connection closed");
        ConnectionState = HubConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public Task OnReconnected(string? connectionId)
    {
        _logger.LogInformation("Hub reconnected: {ConnectionId}", connectionId);
        ConnectionState = HubConnectionState.Connected;
        return Task.CompletedTask;
    }

    public Task OnReconnecting(Exception? exception)
    {
        _logger.LogWarning(exception, "Hub reconnecting");
        ConnectionState = HubConnectionState.Connecting;
        return Task.CompletedTask;
    }
}
