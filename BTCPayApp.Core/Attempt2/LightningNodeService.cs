using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.Attempt2;

public class LightningNodeManager : BaseHostedService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<LightningNodeManager> _logger;
    private readonly OnChainWalletManager _onChainWalletManager;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    private IServiceScope? _nodeScope;
    public LDKNode? Node => _nodeScope?.ServiceProvider.GetService<LDKNode>();
    private LightningNodeState _state = LightningNodeState.Init;

    public LightningNodeState State
    {
        get => _state;
        private set
        {
            if (_state == value)
                return;
            var old = _state;
            _state = value;
            _logger.LogInformation("Lightning node state changed: {State}", _state);
            StateChanged?.Invoke(this, (old, value));
        }
    }

    public event AsyncEventHandler<(LightningNodeState Old, LightningNodeState New)>? StateChanged;

    public LightningNodeManager(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<LightningNodeManager> logger,
        OnChainWalletManager onChainWalletManager,
        BTCPayConnectionManager btcPayConnectionManager,
        IServiceScopeFactory serviceScopeFactory)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _onChainWalletManager = onChainWalletManager;
        _btcPayConnectionManager = btcPayConnectionManager;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task StartNode()
    {
        if (_nodeScope is not null || State is LightningNodeState.Loaded)
            return;
        await _controlSemaphore.WaitAsync();

        try
        {
            if (_nodeScope is null)
            {
                _nodeScope = _serviceScopeFactory.CreateScope();
                _cancellationTokenSource = new CancellationTokenSource();
            }
            await Node.StartAsync(_cancellationTokenSource.Token);

            State = LightningNodeState.Loaded;
        }
        catch (Exception e)
        {
            _nodeScope.Dispose();
            _logger.LogError(e, "Error while starting lightning node");
            _nodeScope = null;
            State = LightningNodeState.Error;
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }

    public async Task StopNode()
    {
        if (_nodeScope is null || State is not LightningNodeState.Loaded)
            return;
        await _controlSemaphore.WaitAsync();
        try
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
            cts.CancelAfter(5000);
            await Node.StopAsync(cts.Token);
           
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while stopping lightning node");
        }
        finally
        {
            _nodeScope?.Dispose();
            _nodeScope = null;
            _controlSemaphore.Release();
            State = LightningNodeState.Stopped;
        }
    }

    public async Task CleanseTask()
    {
        await StopNode();

        if (_nodeScope is not null || State == LightningNodeState.NotConfigured) return;

        await _controlSemaphore.WaitAsync();
        try
        {
            await _onChainWalletManager.RemoveDerivation(WalletDerivation.LightningScripts);
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            context.LightningPayments.RemoveRange(context.LightningPayments);
            context.LightningChannels.RemoveRange(context.LightningChannels);
            context.Settings.RemoveRange(context.Settings.Where(s => new string[]{"ChannelManager","NetworkGraph","Score","lightningconfig"}.Contains(s.Key)));

            await context.SaveChangesAsync();
        }
        finally
        {
            _controlSemaphore.Release();

            State = LightningNodeState.NotConfigured;
        }
    }

    public async Task Generate()
    {
        await _controlSemaphore.WaitAsync();
        try
        {
            if (State == LightningNodeState.NotConfigured &&
                _onChainWalletManager.State != OnChainWalletState.Loaded &&
                _btcPayConnectionManager.ConnectionState != HubConnectionState.Connected)
                throw new InvalidOperationException(
                    "Cannot configure lightning node without on-chain wallet and BTCPay connection");

            await _onChainWalletManager.AddDerivation(WalletDerivation.LightningScripts, "Lightning", null);
            // await _onChainWalletManager.AddDerivation(WalletDerivation.SpendableOutputs, "Lightning Spendables", null);
            State = LightningNodeState.WaitingForConnection;
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }

    private async Task OnConnectionChanged(object? sender, (HubConnectionState Old, HubConnectionState New) state)
    {
        _logger.LogInformation("Connection state changed: {New}, Old: {Old}, Actual:{Actual}", state.New, state.Old,
            _btcPayConnectionManager.ConnectionState);
        if (_btcPayConnectionManager.ConnectionState == HubConnectionState.Connected &&
            State == LightningNodeState.WaitingForConnection)
        {
            State = LightningNodeState.Loading;
        }
        else if (_btcPayConnectionManager.ConnectionState == HubConnectionState.Disconnected &&
                 State is LightningNodeState.Loading or LightningNodeState.Loaded)
        {
             _ = StopNode();
        }
        else
        {
            _logger.LogInformation("Connection state changed: {State} but ln node state stayed {State}", state.New, State);
        }
    }

    private async Task OnChainWalletManagerOnStateChanged(object? sender, (OnChainWalletState Old, OnChainWalletState New) e)
    {
        if (e.New == OnChainWalletState.Loaded)
        {
            State = LightningNodeState.Loading;
        }
    }


    private async Task OnOnStateChanged(object? sender, (LightningNodeState Old, LightningNodeState New) state)
    {
        LightningNodeState? newState = null;
        try
        {
            switch (state.New)
            {
                case LightningNodeState.WaitingForConnection:
                {
                    if (_btcPayConnectionManager.ConnectionState == HubConnectionState.Connected)
                        newState = LightningNodeState.Loading;
                    break;
                }
                case LightningNodeState.Loading:
                    if (_btcPayConnectionManager.ConnectionState != HubConnectionState.Connected)
                    {
                        newState = LightningNodeState.WaitingForConnection;
                        break;
                    }

                    if (_onChainWalletManager.State != OnChainWalletState.Loaded)
                    {
                        newState = LightningNodeState.NotConfigured;
                        break;
                    }

                    if (_onChainWalletManager.WalletConfig is null ||
                        !_onChainWalletManager.WalletConfig.Derivations.ContainsKey(WalletDerivation.LightningScripts))
                    {
                        newState = LightningNodeState.NotConfigured;
                        break;
                    }
                    var result = await _btcPayConnectionManager.HubProxy.IdentifierActive(_onChainWalletManager.WalletConfig
                        .Derivations[WalletDerivation.LightningScripts].Identifier, true).RunSync();
                    if (result)
                    {
                        
                        await StartNode();
                    }
                    else
                    {
                        //TODO: Introduce a new state so that this node knows that another instance is active
                        newState = LightningNodeState.Error;
                    }

                    break;

                case LightningNodeState.Loaded:
                    await _controlSemaphore.WaitAsync();

                    
                    _controlSemaphore.Release();
                    break;
                // case LightningNodeState.Unloading:
                //     _nodeScope?.Dispose();
                //     State = _walletConfig is null
                //         ? LightningNodeState.NotConfigured
                //         : LightningNodeState.WaitingForConnection;
                //     break;
            }
        }
        finally
        {
            if (newState is not null)
                State = newState.Value;
        }
    }


    protected override async Task ExecuteStartAsync(CancellationToken cancellationToken)
    {
        State = LightningNodeState.Init;
        StateChanged += OnOnStateChanged;
        _btcPayConnectionManager.ConnectionChanged += OnConnectionChanged;
        _onChainWalletManager.StateChanged += OnChainWalletManagerOnStateChanged;
    }


    protected override async Task ExecuteStopAsync(CancellationToken cancellationToken)
    {
        _btcPayConnectionManager.ConnectionChanged -= OnConnectionChanged;
        _onChainWalletManager.StateChanged += OnChainWalletManagerOnStateChanged;
        StateChanged -= OnOnStateChanged;
        _nodeScope?.Dispose();
    }
}
