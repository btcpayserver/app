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

public class BTCPayConnectionManager(
    IServiceProvider serviceProvider,
    IAccountManager accountManager,
    AuthenticationStateProvider authStateProvider,
    ILogger<BTCPayConnectionManager> logger,
    BTCPayAppServerClient btcPayAppServerClient,
    IBTCPayAppHubClient btcPayAppServerClientInterface,
    ISecureConfigProvider secureProvider,
    ConfigProvider configProvider,
    SyncService syncService)
    : BaseHostedService(logger), IHubConnectionObserver
{
    private BTCPayConnectionState _connectionState = BTCPayConnectionState.Init;
    private CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IDisposable? _subscription;
    private IBTCPayAppHubServer? _hubProxy;
    public IBTCPayAppHubServer? HubProxy
    {
        get => Connection?.State == HubConnectionState.Connected ? _hubProxy : null;
        private set => _hubProxy = value;
    }
    private HubConnection? Connection { get; set; }
    public Network? ReportedNetwork { get; private set; }
    public string? ReportedNodeInfo { get; set; }
    private bool ForceSlaveMode { get; set; }

    public event AsyncEventHandler<(BTCPayConnectionState Old, BTCPayConnectionState New)>? ConnectionChanged;

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
                logger.LogInformation("Connection state changed: {Old} -> {ConnectionState}", old, _connectionState);
                ConnectionChanged?.Invoke(this, (old, _connectionState));
            }
            finally
            {
               _lock.Release();
            }
        }
    }

    protected override async Task ExecuteStartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConnectionChanged += OnConnectionChanged;
        authStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        btcPayAppServerClient.OnNotifyNetwork += OnNotifyNetwork;
        btcPayAppServerClient.OnNotifyServerEvent += OnNotifyServerEvent;
        btcPayAppServerClient.OnServerNodeInfo += OnServerNodeInfo;
        btcPayAppServerClient.OnMasterUpdated += OnMasterUpdated;
        syncService.EncryptionKeyChanged += EncryptionKeyChanged;
        await OnConnectionChanged(this, (BTCPayConnectionState.Init, BTCPayConnectionState.Init));
    }

    private async Task OnMasterUpdated(object? sender, long? masterId)
    {
        await WrapInLock(async () =>
        {
            if (_cts.IsCancellationRequested)
                return;

            var deviceId = await secureProvider.GetDeviceIdentifier();
            if (masterId is null && ConnectionState == BTCPayConnectionState.ConnectedAsSlave && !ForceSlaveMode)
            {
                logger.LogInformation("OnMasterUpdated: Syncing slave {DeviceId}", deviceId);
                ConnectionState = BTCPayConnectionState.Syncing;
            }
            else if (deviceId == masterId)
            {
                logger.LogInformation("OnMasterUpdated: Setting master to {DeviceId}", deviceId);
                ConnectionState = BTCPayConnectionState.ConnectedAsMaster;
            }
            else if (ConnectionState == BTCPayConnectionState.ConnectedAsMaster && masterId != deviceId)
            {
                logger.LogInformation("OnMasterUpdated: New master {MasterId} - Device: {DeviceId}", masterId, deviceId);
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
        var deviceIdentifier = await secureProvider.GetDeviceIdentifier();
        var newState = e.New;
        try
        {
            var account = accountManager.Account;
            switch (e.New)
            {
                case BTCPayConnectionState.Init:
                    newState = BTCPayConnectionState.WaitingForAuth;
                    break;
                case BTCPayConnectionState.WaitingForAuth:
                    if (account is not null && await accountManager.CheckAuthenticated())
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
                                Task.FromResult(accountManager.Account?.OwnerToken);
                            options.HttpMessageHandlerFactory = serviceProvider
                                .GetService<Func<HttpMessageHandler, HttpMessageHandler>>();
                            options.WebSocketConfiguration =
                                serviceProvider.GetService<Action<ClientWebSocketOptions>>();
                        })
                        .Build();

                    _subscription = connection.Register(btcPayAppServerClientInterface);
                    HubProxy = new ExceptionWrappedHubProxy(connection, logger);

                    if (connection.State == HubConnectionState.Disconnected)
                    {
                        try
                        {
                            await connection.StartAsync();
                        }
                        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized)
                        {
                            await accountManager.Logout();
                            logger.LogInformation("Signed out user because of unauthorized response");
                        }
                        catch (Exception ex)
                        {
                            await Task.Delay(500);
                            if (ex is not TaskCanceledException)
                                logger.LogError(ex, "Error while connecting to hub");
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
                    await syncService.StopSync();
                    if (await syncService.EncryptionKeyRequiresImport())
                    {
                        newState = BTCPayConnectionState.WaitingForEncryptionKey;
                        logger.LogWarning(
                            "Existing state found but encryption key is missing, waiting until key is provided");
                    }
                    else
                    {
                        //check if we are the master previously to process outbox items
                        var masterDevice = await HubProxy!.GetCurrentMaster();
                        if (deviceIdentifier == masterDevice)
                        {
                            logger.LogInformation("Syncing master to remote: {DeviceId}", deviceIdentifier);
                            await syncService.SyncToRemote(CancellationToken.None);
                        }
                        else
                        {
                            logger.LogInformation("Syncing to local. Master: {MasterId} - Device: {DeviceId}", masterDevice, deviceIdentifier);
                            await syncService.SyncToLocal();
                        }
                        newState = BTCPayConnectionState.ConnectedFinishedInitialSync;

                        var config = await configProvider.Get<BTCPayAppConfig>(BTCPayAppConfig.Key);
                        if (!string.IsNullOrEmpty(config?.CurrentStoreId))
                        {
                            await accountManager.SetCurrentStoreId(config.CurrentStoreId);
                        }
                    }
                    break;
                case BTCPayConnectionState.ConnectedFinishedInitialSync:
                    if (ForceSlaveMode)
                    {
                        await HubProxy!.DeviceMasterSignal(deviceIdentifier, false);
                        ForceSlaveMode = false;
                        newState = BTCPayConnectionState.ConnectedAsSlave;
                    }
                    else if (!await HubProxy!.DeviceMasterSignal(deviceIdentifier, true))
                    {
                        newState = BTCPayConnectionState.ConnectedAsSlave;
                    }
                    break;
                case BTCPayConnectionState.ConnectedAsMaster:
                    await syncService.StartSync(false);
                    break;
                case BTCPayConnectionState.ConnectedAsSlave:
                    await syncService.StartSync(true);
                    break;
                case BTCPayConnectionState.Disconnected:
                    newState = BTCPayConnectionState.WaitingForAuth;
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while changing connection state from {Old} to {New}", e.Old, e.New);
            throw;
        }
        finally
        {
            _ = Task.Run(() => ConnectionState = newState);
        }
    }

    private Task OnServerNodeInfo(object? sender, string? e)
    {
        ReportedNodeInfo = e;
        return Task.CompletedTask;
    }

    private Task OnNotifyServerEvent(object? sender, ServerEvent e)
    {
        logger.LogInformation("OnNotifyServerEvent: {Type} - {Details}", e.Type, e.ToString());
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
                var authState = await accountManager.CheckAuthenticated();
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
                logger.LogError(e, "Error while handling authentication state change");
            }
        }, _cts.Token);
    }

    private async Task Kill()
    {
        if (Connection is not null)
        {
            logger.LogWarning("Killing connection");
        }
        var conn = Connection;
        Connection = null;
        if (conn is not null)
            await conn.StopAsync();
        _subscription?.Dispose();
        _subscription = null;
        HubProxy = null;
        await syncService.StopSync();
    }


    protected override async Task ExecuteStopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        if (_connectionState == BTCPayConnectionState.ConnectedAsMaster)
        {
            var deviceId = await secureProvider.GetDeviceIdentifier();
            logger.LogInformation("Sending device master signal to turn off {DeviceId}", deviceId);
            await syncService.StopSync();
            await syncService.SyncToRemote(CancellationToken.None);
            if (HubProxy is not null)
            {
                await HubProxy.DeviceMasterSignal(deviceId, false);
            }
        }

        await Kill();
        authStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        btcPayAppServerClient.OnNotifyNetwork -= OnNotifyNetwork;

        syncService.EncryptionKeyChanged -= EncryptionKeyChanged;
        ConnectionChanged -= OnConnectionChanged;
    }

    public Task OnClosed(Exception? exception)
    {
        logger.LogError(exception, "Hub connection closed");
        if (Connection?.State == HubConnectionState.Disconnected && ConnectionState != BTCPayConnectionState.Connecting)
        {
            ConnectionState = BTCPayConnectionState.Disconnected;
        }

        return Task.CompletedTask;
    }

    public Task OnReconnected(string? connectionId)
    {
        logger.LogInformation("Hub connection reconnected");
        ConnectionState = BTCPayConnectionState.Syncing;
        return Task.CompletedTask;
    }

    public Task OnReconnecting(Exception? exception)
    {
        logger.LogWarning(exception, "Hub connection reconnecting");
        ConnectionState = BTCPayConnectionState.Connecting;
        return Task.CompletedTask;
    }

    public async Task SwitchToSlave()
    {
        if (_connectionState == BTCPayConnectionState.ConnectedAsMaster)
        {
            ForceSlaveMode = true;
            var deviceId = await secureProvider.GetDeviceIdentifier();
            logger.LogInformation("Sending device master signal to turn off {DeviceId}", deviceId);
            await syncService.StopSync();
            await syncService.SyncToRemote(CancellationToken.None);
            await HubProxy!.DeviceMasterSignal(deviceId, false);
        }
    }
}
