using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using nldksample.LDK;

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


    public async Task StopNode()
    {
        await _controlSemaphore.WaitAsync();
        try
        {
            if (_nodeScope is null || State == LightningNodeState.Loaded)
                return;
            _nodeScope.Dispose();
            _nodeScope = null;
        }
        finally
        {
            _controlSemaphore.Release();
            State = LightningNodeState.Init;
        }
    }
    
    public async Task CleanseTask()
    {
        
        await _controlSemaphore.WaitAsync();
        try
        {
            if (_nodeScope is not null || State == LightningNodeState.NodeNotConfigured)
            {
                return;
            }
            
            
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
            
            State = LightningNodeState.NodeNotConfigured;
        }
    }

    public async Task Generate()
    {
        await _controlSemaphore.WaitAsync();
        try
        {
            if (State == LightningNodeState.NodeNotConfigured &&
                _onChainWalletManager.State != OnChainWalletState.Loaded &&
                _btcPayConnectionManager.Connection?.State != HubConnectionState.Connected)
                throw new InvalidOperationException(
                    "Cannot configure lightning node without on-chain wallet and BTCPay connection");

            await _onChainWalletManager.AddDerivation(WalletDerivation.LightningScripts, "Lightning", null);
            State = LightningNodeState.WaitingForConnection;
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }

    private async Task BTCPayConnectionManagerOnConnectionManagerChanged(object? sender,
        HubConnectionState hubConnectionState)
    {
        if (_btcPayConnectionManager.Connection?.State == HubConnectionState.Connected &&
            State == LightningNodeState.WaitingForConnection)
        {
            State = LightningNodeState.Loading;
        }
        else if (_btcPayConnectionManager.Connection?.State == HubConnectionState.Disconnected &&
                 State is LightningNodeState.Loading or LightningNodeState.Loaded)
        {
            // State = LightningNodeState.Unloading;
        }
    }

    private async Task OnChainWalletManagerOnStateChanged(object? sender,
        (OnChainWalletState Old, OnChainWalletState New) e)
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
            await _controlSemaphore.WaitAsync();
            switch (state.New)
            {
                case LightningNodeState.WaitingForConnection:
                {
                    if (_btcPayConnectionManager?.Connection?.State == HubConnectionState.Connected)
                        newState = LightningNodeState.Loading;
                    break;
                }
                case LightningNodeState.Loading:
                    if (_btcPayConnectionManager?.Connection?.State != HubConnectionState.Connected)
                    {
                        newState = LightningNodeState.WaitingForConnection;
                        break;
                    }

                    if (_onChainWalletManager.State != OnChainWalletState.Loaded)
                    {
                        newState = LightningNodeState.NodeNotConfigured;
                        break;
                    }

                    if (_onChainWalletManager.WalletConfig is null ||
                        !_onChainWalletManager.WalletConfig.Derivations.ContainsKey(WalletDerivation.LightningScripts))
                    {
                        newState = LightningNodeState.NodeNotConfigured;
                        break;
                    }

                    if (_nodeScope is null)
                    {
                        _nodeScope = _serviceScopeFactory.CreateScope();
                        _cancellationTokenSource = new CancellationTokenSource();
                    }

                    try
                    {
                        await Node.StartAsync(_cancellationTokenSource.Token);
                        newState = LightningNodeState.Loaded;
                    }
                    catch (Exception e)
                    {
                        _nodeScope.Dispose();
                        _logger.LogError(e, "Error while starting lightning node");
                        _nodeScope = null;
                        State = LightningNodeState.Error;
                    }

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
            _controlSemaphore.Release();
            if (newState is not null)
                State = newState.Value;
        }
    }


    protected override async Task ExecuteStartAsync(CancellationToken cancellationToken)
    {
        _onChainWalletManager.StateChanged += OnChainWalletManagerOnStateChanged;
        _btcPayConnectionManager.ConnectionChanged += BTCPayConnectionManagerOnConnectionManagerChanged;
        StateChanged += OnOnStateChanged;
        State = LightningNodeState.Loading;
    }


    protected override async Task ExecuteStopAsync(CancellationToken cancellationToken)
    {
        _onChainWalletManager.StateChanged += OnChainWalletManagerOnStateChanged;
        _btcPayConnectionManager.ConnectionChanged -= BTCPayConnectionManagerOnConnectionManagerChanged;

        StateChanged -= OnOnStateChanged;
        _nodeScope?.Dispose();
    }
}