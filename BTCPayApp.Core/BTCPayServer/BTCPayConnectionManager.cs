using System.Net;
using System.Net.WebSockets;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Backup;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using TypedSignalR.Client;

namespace BTCPayApp.Core.BTCPayServer;

public class BTCPayConnectionManager : BaseHostedService, IHubConnectionObserver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAccountManager _accountManager;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<BTCPayConnectionManager> _logger;
    private readonly BTCPayAppServerClient _btcPayAppServerClient;
    private readonly IBTCPayAppHubClient _btcPayAppServerClientInterface;
    private readonly ConfigProvider _configProvider;
    private readonly SyncService _syncService;
    private IDisposable? _subscription;

    public IBTCPayAppHubServer? HubProxy
    {
        get => Connection?.State == HubConnectionState.Connected ? _hubProxy : null;
        private set => _hubProxy = value;
    }

    private HubConnection? Connection { get; set; }
    public Network? ReportedNetwork { get; private set; }
    public string ReportedNodeInfo { get; set; }
    public bool ForceSlaveMode { get; set; }

    public event AsyncEventHandler<(BTCPayConnectionState Old, BTCPayConnectionState New)>? ConnectionChanged;
    private BTCPayConnectionState _connectionState = BTCPayConnectionState.Init;

    private readonly SemaphoreSlim _lock = new(1, 1);
    public BTCPayConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            _lock.Wait();
            try
            {
                if (_connectionState == value) return;
                var old = _connectionState;
                _connectionState = value;
                _logger.LogInformation($"Connection state changed: {_connectionState} from {old}" );
                ConnectionChanged?.Invoke(this, (old, _connectionState));
            }
            finally
            {
               _lock.Release();
            }
        }
    }

    // TODO: Make this a primary constructor
    public BTCPayConnectionManager(
        IServiceProvider serviceProvider,
        IAccountManager accountManager,
        AuthenticationStateProvider authStateProvider,
        ILogger<BTCPayConnectionManager> logger,
        BTCPayAppServerClient btcPayAppServerClient,
        IBTCPayAppHubClient btcPayAppServerClientInterface,
        ConfigProvider configProvider,
        SyncService syncService) : base(logger)
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
    private IBTCPayAppHubServer? _hubProxy;

    protected override async Task ExecuteStartAsync(CancellationToken cancellationToken)
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
        _ = MonitorHubConnection(_cts.Token);
    }

    // TODO: Remove this
    private async Task MonitorHubConnection(CancellationToken cancellationToken)
    {
        // while (!cancellationToken.IsCancellationRequested)
        // {
        //     await WrapInLock(async () =>
        //         {
        //             if (Connection?.State is HubConnectionState.Disconnected)
        //             {
        //                 await OnClosed(new Exception("MonitorHubConnection"));
        //             }
        //         }
        //         , cancellationToken);
        // }
        //
        // await Task.Delay(500, cancellationToken);
    }

    private async Task OnMasterUpdated(object? sender, long? e)
    {
        await WrapInLock(async () =>
        {
            if (_cts.IsCancellationRequested)
                return;
            if (e is null && ConnectionState == BTCPayConnectionState.ConnectedAsSlave && !ForceSlaveMode)
            {
                ConnectionState = BTCPayConnectionState.Syncing;
            }
            else if (await _configProvider.GetDeviceIdentifier() == e)
            {
                ConnectionState = BTCPayConnectionState.ConnectedAsMaster;
            }
            else if (ConnectionState == BTCPayConnectionState.ConnectedAsMaster && e != await _configProvider.GetDeviceIdentifier())
            {
                ConnectionState = BTCPayConnectionState.Syncing;
            }
        }, _cts.Token);
    }

    private async Task EncryptionKeyChanged(object? sender)
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        await WrapInLock(async () =>
        {
            if (_connectionState == BTCPayConnectionState.WaitingForEncryptionKey)
            {
                ConnectionState = BTCPayConnectionState.Syncing;
            }
        }, _cts.Token);
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    }

    private async Task OnConnectionChanged(object? sender, (BTCPayConnectionState Old, BTCPayConnectionState New) e)
    {
        var deviceIdentifier = await _configProvider.GetDeviceIdentifier();
        var newState = e.New;
        try
        {
            var account = _accountManager.GetAccount();
            switch (e.New)
            {
                case BTCPayConnectionState.Init:
                    newState = BTCPayConnectionState.WaitingForAuth;
                    break;
                case BTCPayConnectionState.WaitingForAuth:
                    if (account is not null && await _accountManager.CheckAuthenticated())
                    {
                        newState = BTCPayConnectionState.Connecting;
                    }
                    break;
                case BTCPayConnectionState.Connecting:
                    if (account is null)
                    {
                        newState = BTCPayConnectionState.WaitingForAuth;
                        break;
                    }
                    await Kill();
                    var url = new Uri(new Uri(account.BaseUri), "hub/btcpayapp").ToString();
                    var connection = new HubConnectionBuilder()
                        .AddNewtonsoftJsonProtocol(options =>
                        {
                            NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(options.PayloadSerializerSettings);
                            options.PayloadSerializerSettings.Converters.Add(new global::BTCPayServer.Lightning.JsonConverters.LightMoneyJsonConverter());
                        })
                        .WithUrl(url, options =>
                        {
                            options.AccessTokenProvider = () =>
                                Task.FromResult(_accountManager.GetAccount()?.AccessToken);
                            options.HttpMessageHandlerFactory = _serviceProvider
                                .GetService<Func<HttpMessageHandler, HttpMessageHandler>>();
                            options.WebSocketConfiguration =
                                _serviceProvider.GetService<Action<ClientWebSocketOptions>>();
                        })
                        .Build();

                    _subscription = connection.Register(_btcPayAppServerClientInterface);
                    HubProxy = new ExceptionWrappedHubProxy(connection, _logger);

                    if (connection.State == HubConnectionState.Disconnected)
                    {
                        try
                        {
                            await connection.StartAsync();
                        }
                        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized)
                        {
                            await _accountManager.Logout();
                            _logger.LogInformation("Signed out user because of unauthorized response");
                        }
                        catch (Exception ex)
                        {
                            await Task.Delay(500);
                            if (ex is not TaskCanceledException)
                                _logger.LogError(ex, "Error while connecting to hub");
                        }
                    }
                    Connection = connection;
                    newState = Connection.State switch
                    {
                        HubConnectionState.Connected => BTCPayConnectionState.Syncing,
                        HubConnectionState.Connecting => BTCPayConnectionState.Connecting,
                        _ => BTCPayConnectionState.WaitingForAuth
                    };
                    break;
                case BTCPayConnectionState.Syncing:
                    await _syncService.StopSync();
                    if (await _syncService.EncryptionKeyRequiresImport())
                    {
                        newState = BTCPayConnectionState.WaitingForEncryptionKey;
                        _logger.LogWarning(
                            "Existing state found but encryption key is missing, waiting until key is provided");
                    }
                    else
                    {
                        //check if we are the master previously to process outbox items
                        var masterDevice = await HubProxy.GetCurrentMaster();
                        if (deviceIdentifier == masterDevice)
                        {
                            await _syncService.SyncToRemote(CancellationToken.None);
                        }
                        else
                        {
                            await _syncService.SyncToLocal();
                        }
                        newState = BTCPayConnectionState.ConnectedFinishedInitialSync;
                    }
                    break;
                case BTCPayConnectionState.ConnectedFinishedInitialSync:
                    if (ForceSlaveMode)
                    {
                        await HubProxy.DeviceMasterSignal(deviceIdentifier, false);
                        ForceSlaveMode = false;
                        newState = BTCPayConnectionState.ConnectedAsSlave;
                    }
                    else if (!await HubProxy.DeviceMasterSignal(deviceIdentifier, true))
                    {
                        newState = BTCPayConnectionState.ConnectedAsSlave;
                    }
                    break;
                case BTCPayConnectionState.ConnectedAsMaster:
                    await _syncService.StartSync(false);
                    break;
                case BTCPayConnectionState.ConnectedAsSlave:
                    await _syncService.StartSync(true);
                    break;
                case BTCPayConnectionState.Disconnected:
                    newState = BTCPayConnectionState.WaitingForAuth;
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while changing connection state from {Old} to {New}", e.Old, e.New);
            throw;
        }
        finally
        {
            _ = Task.Run(() => ConnectionState = newState);
        }
    }

    private Task OnServerNodeInfo(object? sender, string e)
    {
        ReportedNodeInfo = e;
        return Task.CompletedTask;
    }

    private Task OnNotifyServerEvent(object? sender, ServerEvent e)
    {
        _logger.LogInformation("OnNotifyServerEvent: {Type} - {Details}", e.Type, e.ToString());
        return Task.CompletedTask;
    }

    private Task OnNotifyNetwork(object? sender, string e)
    {
        ReportedNetwork = Network.GetNetwork(e);
        return Task.CompletedTask;
    }

    private async void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        await WrapInLock(async () =>
        {
            try
            {
                await task;
                var authState = await _accountManager.CheckAuthenticated();
                if (ConnectionState == BTCPayConnectionState.WaitingForAuth && authState)
                {

                    ConnectionState = BTCPayConnectionState.Connecting;
                }
                else if (ConnectionState > BTCPayConnectionState.WaitingForAuth && !authState)
                {
                    ConnectionState = BTCPayConnectionState.WaitingForAuth;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while handling authentication state change");
            }
        }, _cts.Token);
    }

    private async Task Kill()
    {
        if (Connection is not null)
        {
            _logger.LogWarning("Killing connection");
        }
        var conn = Connection;
        Connection = null;
        if (conn is not null)
            await conn.StopAsync();
        _subscription?.Dispose();
        _subscription = null;
        HubProxy = null;
        await _syncService.StopSync();
    }


    protected override async Task ExecuteStopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        if (_connectionState == BTCPayConnectionState.ConnectedAsMaster)
        {
            _logger.LogInformation("Sending device master signal to turn off");
            var deviceIdentifier = await _configProvider.GetDeviceIdentifier();
            await _syncService.StopSync();
            await _syncService.SyncToRemote(CancellationToken.None);
            if (HubProxy is not null)
            {
                await HubProxy.DeviceMasterSignal(deviceIdentifier, false);
            }
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
        if (Connection?.State == HubConnectionState.Disconnected && ConnectionState != BTCPayConnectionState.Connecting)
        {
            ConnectionState = BTCPayConnectionState.Disconnected;
        }

        return Task.CompletedTask;
    }

    public Task OnReconnected(string? connectionId)
    {
        _logger.LogInformation("Hub connection reconnected");
        ConnectionState = BTCPayConnectionState.Syncing;
        return Task.CompletedTask;
    }

    public Task OnReconnecting(Exception? exception)
    {
        _logger.LogWarning(exception, "Hub connection reconnecting");
        ConnectionState = BTCPayConnectionState.Connecting;
        return Task.CompletedTask;
    }

    public async Task SwitchToSlave()
    {
        if (_connectionState == BTCPayConnectionState.ConnectedAsMaster)
        {
            ForceSlaveMode = true;
            _logger.LogInformation("Sending device master signal to turn off");
            await _syncService.StopSync();
            await _syncService.SyncToRemote( CancellationToken.None);
            await HubProxy.DeviceMasterSignal(await _configProvider.GetDeviceIdentifier(), false);
        }
    }
}
