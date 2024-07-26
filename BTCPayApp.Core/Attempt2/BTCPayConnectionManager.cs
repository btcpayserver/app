using System.Net;
using System.Net.Http.Headers;
using BTCPayApp.CommonServer;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.VSS;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using TypedSignalR.Client;

namespace BTCPayApp.Core.Attempt2;

public static class ConfigHelpers
{

    public static async Task<T> GetOrSet<T>(this ISecureConfigProvider secureConfigProvider, string key, Func<Task<T>> factory)
    {
        var value = await secureConfigProvider.Get<T>(key);
        if (value is null)
        {
            value = await factory();
            await secureConfigProvider.Set(key, value);
        }

        return value;

    }
}

public class BTCPayConnectionManager : IHostedService, IHubConnectionObserver
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAccountManager _accountManager;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<BTCPayConnectionManager> _logger;
    private readonly BTCPayAppServerClient _btcPayAppServerClient;
    private readonly IBTCPayAppHubClient _btcPayAppServerClientInterface;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecureConfigProvider _secureConfigProvider;
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
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAccountManager accountManager,
        AuthenticationStateProvider authStateProvider,
        ILogger<BTCPayConnectionManager> logger,
        BTCPayAppServerClient btcPayAppServerClient,
        IBTCPayAppHubClient btcPayAppServerClientInterface,
        IHttpClientFactory httpClientFactory,
        ISecureConfigProvider secureConfigProvider)
    {
        _dbContextFactory = dbContextFactory;
        _accountManager = accountManager;
        _authStateProvider = authStateProvider;
        _logger = logger;
        _btcPayAppServerClient = btcPayAppServerClient;
        _btcPayAppServerClientInterface = btcPayAppServerClientInterface;
        _httpClientFactory = httpClientFactory;
        _secureConfigProvider = secureConfigProvider;
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
    
    private async Task<IDataProtector> GetDataProtector()
    {
        var key = await _secureConfigProvider.GetOrSet("encryptionKey", async () => Convert.ToHexString(RandomUtils.GetBytes(32)).ToLowerInvariant());
        return new SingleKeyDataProtector(Convert.FromHexString(key));
    }

    public async Task<IVSSAPI> GetVSSAPI()
    {
        if (Connection is null)
            throw new InvalidOperationException("Connection is not established");
        var vssUri = new Uri(new Uri(_accountManager.GetAccount().BaseUri), "vss/");
        var httpClient = _httpClientFactory.CreateClient("vss");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accountManager.GetAccount().AccessToken);
        var vssClient =  new HttpVSSAPIClient(vssUri, httpClient);
        var protector = await GetDataProtector();
        return new VSSApiEncryptorClient(vssClient, protector);
    }
    
    private async Task OnServerNodeInfo(object? sender, string e)
    {
        ReportedNodeInfo = e;
    }

    private async Task OnNotifyServerEvent(object? sender, ServerEvent e)
    {
        _logger.LogInformation("OnNotifyServerEvent: {Type} - {Details}", e.Type, e.ToString());
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

    private async Task MarkConnected()
    {
        await new RemoteToLocalSyncService(_dbContextFactory,this).Sync();
        ConnectionState = HubConnectionState.Connected;
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

                    await MarkConnected();
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

    public async Task OnReconnected(string? connectionId)
    {
        _logger.LogInformation("Hub reconnected: {ConnectionId}", connectionId);
        await MarkConnected();
    }

    public Task OnReconnecting(Exception? exception)
    {
        _logger.LogWarning(exception, "Hub reconnecting");
        ConnectionState = HubConnectionState.Connecting;
        return Task.CompletedTask;
    }
}
