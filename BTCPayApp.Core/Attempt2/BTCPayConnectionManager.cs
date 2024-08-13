using System.Net;
using System.Net.WebSockets;
using BTCPayApp.CommonServer;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using TypedSignalR.Client;

namespace BTCPayApp.Core.Attempt2;

public class BTCPayConnectionManager : IHostedService, IHubConnectionObserver
{
    private const string ConfigDeviceIdentifierKey = "deviceIdentifier";
    private readonly IServiceProvider _serviceProvider;
    private readonly IAccountManager _accountManager;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<BTCPayConnectionManager> _logger;
    private readonly BTCPayAppServerClient _btcPayAppServerClient;
    private readonly IBTCPayAppHubClient _btcPayAppServerClientInterface;
    private readonly IConfigProvider _configProvider;
    private readonly SyncService _syncService;
    private IDisposable? _subscription;

    public IBTCPayAppHubServer? HubProxy { get; private set; }
    private HubConnection? Connection { get; set; }
    public Network? ReportedNetwork { get; private set; }

    public string ReportedNodeInfo { get; set; }

    public event AsyncEventHandler<(BTCPayConnectionState Old, BTCPayConnectionState New)>? ConnectionChanged;
    private BTCPayConnectionState _connectionState = BTCPayConnectionState.Init;

    public BTCPayConnectionState ConnectionState
    {
        get => _connectionState;
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
        IServiceProvider serviceProvider,
        IAccountManager accountManager,
        AuthenticationStateProvider authStateProvider,
        ILogger<BTCPayConnectionManager> logger,
        BTCPayAppServerClient btcPayAppServerClient,
        IBTCPayAppHubClient btcPayAppServerClientInterface,
        IConfigProvider configProvider,
        SyncService syncService)
    {
        _serviceProvider = serviceProvider;
        _accountManager = accountManager;
        _authStateProvider = authStateProvider;
        _logger = logger;
        _btcPayAppServerClient = btcPayAppServerClient;
        _btcPayAppServerClientInterface = btcPayAppServerClientInterface;
        _configProvider = configProvider;
        _syncService = syncService;
    }

private CancellationTokenSource _cts = new();
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConnectionChanged += OnConnectionChanged;
        _authStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _btcPayAppServerClient.OnNotifyNetwork += OnNotifyNetwork;
        _btcPayAppServerClient.OnNotifyServerEvent += OnNotifyServerEvent;
        _btcPayAppServerClient.OnServerNodeInfo += OnServerNodeInfo;
        _btcPayAppServerClient.OnMasterUpdated += OnMasterUpdated;
        _syncService.EncryptionKeyChanged += EncryptionKeyChanged;
        await OnConnectionChanged(this, (BTCPayConnectionState.Init, BTCPayConnectionState.Init));
        await MonitorHubConnection(_cts.Token);
        
        
    }

    private async Task MonitorHubConnection(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if(Connection?.State is HubConnectionState.Disconnected)
            {
                ConnectionState = BTCPayConnectionState.Disconnected;
            }
            
            await Task.Delay(500, cancellationToken);
            
            
        }
    }

    private async Task OnMasterUpdated(object? sender, long? e)
    {
        if (e is null && ConnectionState == BTCPayConnectionState.ConnectedAsSlave && !ForceSlaveMode)
        {
            ConnectionState = BTCPayConnectionState.Syncing;
        }else if (await GetDeviceIdentifier() == e)
        {
            ConnectionState = BTCPayConnectionState.ConnectedAsMaster;
            
        }else if (ConnectionState == BTCPayConnectionState.ConnectedAsMaster && e != await GetDeviceIdentifier())
        {
            ConnectionState = BTCPayConnectionState.Syncing;
        }
        
    }

    private async Task EncryptionKeyChanged(object? sender)
    {
        if (_connectionState == BTCPayConnectionState.WaitingForEncryptionKey)
        {
            ConnectionState = BTCPayConnectionState.Syncing;
        }
    }

    public  async Task<long> GetDeviceIdentifier()
    {
        return await _configProvider.GetOrSet(ConfigDeviceIdentifierKey,
            async () => RandomUtils.GetInt64(), false);
    }


    private async Task OnConnectionChanged(object? sender, (BTCPayConnectionState Old, BTCPayConnectionState New) e)
    {
        var account = _accountManager.GetAccount();
        switch (e.New)
        {
            case BTCPayConnectionState.Init:
                ConnectionState = BTCPayConnectionState.WaitingForAuth;
                break;
            case BTCPayConnectionState.WaitingForAuth:

                await Kill();
                if (account is not null)
                {
                    ConnectionState = BTCPayConnectionState.Connecting;
                }

                break;
            case BTCPayConnectionState.Connecting:
                if (account is null)
                {
                    ConnectionState = BTCPayConnectionState.WaitingForAuth;
                    break;
                }
                
                await Kill();

                if (Connection is null)
                {
                    Connection = new HubConnectionBuilder()
                        .AddNewtonsoftJsonProtocol(options =>
                        {
                            NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(
                                options.PayloadSerializerSettings);
                            options.PayloadSerializerSettings.Converters.Add(new BTCPayServer.Lightning.JsonConverters.LightMoneyJsonConverter());
                            
                            
                        })
                        .WithUrl(new Uri(new Uri(account.BaseUri), "hub/btcpayapp").ToString(),
                            options =>
                            {
                                options.AccessTokenProvider = () =>
                                    Task.FromResult(_accountManager.GetAccount()?.AccessToken);
                                options.HttpMessageHandlerFactory = _serviceProvider.GetService<Func<HttpMessageHandler,HttpMessageHandler>>();
                                options.WebSocketConfiguration = _serviceProvider.GetService<Action<ClientWebSocketOptions>>();

                            })
                        .Build();

                    _subscription = Connection.Register(_btcPayAppServerClientInterface);
                    HubProxy = Connection.CreateHubProxy<IBTCPayAppHubServer>();
                }

                if (Connection.State == HubConnectionState.Disconnected)
                {
                    try
                    {
                        await Connection.StartAsync();
                        if (Connection.State == HubConnectionState.Connected)
                        {
                            ConnectionState = BTCPayConnectionState.Syncing;
                        }
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized)
                    {
                        await _accountManager.RefreshAccess();
                        ConnectionState = BTCPayConnectionState.WaitingForAuth;
                    }
                    catch (Exception ex)
                    {
                        await Task.Delay(500);
                        ConnectionState = BTCPayConnectionState.WaitingForAuth;
                        if (ex is not TaskCanceledException)
                            _logger.LogError(ex, "Error while connecting to hub");
                    }
                }
                break;
            case BTCPayConnectionState.Syncing:
                await _syncService.StopSync();
                if (await _syncService.EncryptionKeyRequiresImport())
                {
                    ConnectionState = BTCPayConnectionState.WaitingForEncryptionKey;
                    _logger.LogWarning("Existing state found but encryption key is missing, waiting until key is provided");
                }
                else
                {
                    //check if we are the master previosuly to process outbox items
                    var masterDevice = await HubProxy.GetCurrentMaster();
                    if (await GetDeviceIdentifier() == masterDevice)
                    {
                        await _syncService.SyncToRemote(await GetDeviceIdentifier(), CancellationToken.None);
                    }
                    else
                    {
                        await _syncService.SyncToLocal();
                    }

                    ConnectionState = BTCPayConnectionState.ConnectedFinishedInitialSync;
                }
                break;
            case BTCPayConnectionState.ConnectedFinishedInitialSync:
                var deviceIdentifier = await GetDeviceIdentifier();
                if (ForceSlaveMode)
                {
                    await HubProxy.DeviceMasterSignal(deviceIdentifier, false);
                    ForceSlaveMode = false;
                    ConnectionState = BTCPayConnectionState.ConnectedAsSlave;
                }

                else if (!await HubProxy.DeviceMasterSignal(deviceIdentifier, true))
                {
                    ConnectionState = BTCPayConnectionState.ConnectedAsSlave;
                }
            
               
                break;
            case BTCPayConnectionState.ConnectedAsMaster:
                await _syncService.StartSync(false, await GetDeviceIdentifier());
                break;
            case BTCPayConnectionState.ConnectedAsSlave:
                await _syncService.StartSync(true, await GetDeviceIdentifier());
                break;
            case BTCPayConnectionState.Disconnected:
                ConnectionState = BTCPayConnectionState.WaitingForAuth;
                break;
        }
    }

    public bool ForceSlaveMode { get; set; }

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
            var authState = await _accountManager.CheckAuthenticated();
            if (ConnectionState == BTCPayConnectionState.WaitingForAuth && authState)
            {
                ConnectionState = BTCPayConnectionState.Connecting;
            }else if (ConnectionState > BTCPayConnectionState.WaitingForAuth && !authState)
            {
                ConnectionState = BTCPayConnectionState.WaitingForAuth;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while handling authentication state change");
        }
    }


    private async Task Kill()
    {
        var conn = Connection;
        Connection = null;
        if (conn is not null)
            await conn.StopAsync();
        _subscription?.Dispose();
        _subscription = null;
        HubProxy = null;
        await _syncService.StopSync();
    }


    public async Task StopAsync(CancellationToken cancellationToken)
    {
        
        await _cts.CancelAsync();
        if (_connectionState == BTCPayConnectionState.ConnectedAsMaster)
        {
            _logger.LogInformation("Sending device master signal to turn off");
            var deviceIdentifier = await GetDeviceIdentifier();
            await _syncService.StopSync();
            await _syncService.SyncToRemote(await GetDeviceIdentifier(), CancellationToken.None);
            await HubProxy.DeviceMasterSignal(deviceIdentifier, false);
        }

        await Kill();
        _authStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        _btcPayAppServerClient.OnNotifyNetwork -= OnNotifyNetwork;

        _syncService.EncryptionKeyChanged -= EncryptionKeyChanged;
        ConnectionChanged -= OnConnectionChanged;
    }

    public Task OnClosed(Exception? exception)
    {
        _logger.LogError(exception, "Hub connection closed");
        if (Connection?.State == HubConnectionState.Disconnected)
        {
            ConnectionState = BTCPayConnectionState.Disconnected;
        }

        return Task.CompletedTask;
    }

    public async Task OnReconnected(string? connectionId)
    {
        _logger.LogInformation("Hub connection reconnected");
        ConnectionState = BTCPayConnectionState.Syncing;
    }

    public async Task OnReconnecting(Exception? exception)
    {
        _logger.LogWarning(exception, "Hub connection reconnecting");
        ConnectionState = BTCPayConnectionState.Connecting;
    }

    public async Task SwitchToSlave()
    {
        if (_connectionState == BTCPayConnectionState.ConnectedAsMaster)
        {
            ForceSlaveMode = true;
            _logger.LogInformation("Sending device master signal to turn off");
            var deviceIdentifier = await GetDeviceIdentifier();
            await _syncService.StopSync();
            await _syncService.SyncToRemote(await GetDeviceIdentifier(), CancellationToken.None);
            await HubProxy.DeviceMasterSignal(deviceIdentifier, false);
        }
    }
}
