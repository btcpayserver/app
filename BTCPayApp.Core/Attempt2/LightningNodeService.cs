using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.Attempt2;

public class LightningNodeManager : BaseHostedService
{
    public const string PaymentMethodId = "BTC-LN";

    private readonly IAccountManager _accountManager;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<LightningNodeManager> _logger;
    private readonly OnChainWalletManager _onChainWalletManager;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    
    private IServiceScope? _nodeScope;
    public LDKNode? Node => _nodeScope?.ServiceProvider.GetService<LDKNode>();
    private LightningNodeState _state = LightningNodeState.Init;
    private bool IsHubConnected => _btcPayConnectionManager.ConnectionState is BTCPayConnectionState.ConnectedAsMaster;
    private bool IsOnchainConfigured => _onChainWalletManager.WalletConfig is not null;
    private bool IsOnchainLightningDerivationConfigured => _onChainWalletManager.WalletConfig?.Derivations.ContainsKey(WalletDerivation.LightningScripts) is true;
    public bool CanConfigureLightningNode => IsHubConnected && IsOnchainConfigured && !IsOnchainLightningDerivationConfigured && State == LightningNodeState.NotConfigured;
    public string? ConnectionString => IsOnchainLightningDerivationConfigured && _accountManager.GetUserInfo() is {} acc
        ? $"type=app;user={acc.UserId}": null;

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
        IAccountManager accountManager,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<LightningNodeManager> logger,
        OnChainWalletManager onChainWalletManager,
        BTCPayConnectionManager btcPayConnectionManager,
        IServiceScopeFactory serviceScopeFactory)
    {
        _accountManager = accountManager;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _onChainWalletManager = onChainWalletManager;
        _btcPayConnectionManager = btcPayConnectionManager;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task StartNode()
    {
        if (_nodeScope is not null || State is LightningNodeState.Loaded or LightningNodeState.NotConfigured || _onChainWalletManager.State is not OnChainWalletState.Loaded)
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
            if (Node != null) await Node.StopAsync(cts.Token);
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
            // context.OutboxItems.RemoveRange(context.OutboxItems);
            context.Settings.RemoveRange(context.Settings.Where(s => s.Key.StartsWith("ln:")));
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
            if (State != LightningNodeState.NotConfigured) return;
            if (!IsHubConnected)
                throw new InvalidOperationException("Cannot configure lightning node without BTCPay connection");
            if (!IsOnchainConfigured)
                throw new InvalidOperationException("Cannot configure lightning node without on-chain wallet configuration");
            if (IsOnchainLightningDerivationConfigured)
                throw new InvalidOperationException("On-chain wallet is already configured with a lightning derivation");

            await _onChainWalletManager.AddDerivation(WalletDerivation.LightningScripts, "Lightning", null);
            // await _onChainWalletManager.AddDerivation(WalletDerivation.SpendableOutputs, "Lightning Spendables", null);
            State = LightningNodeState.WaitingForConnection;
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }

    private async Task OnConnectionChanged(object? sender, (BTCPayConnectionState Old, BTCPayConnectionState New) valueTuple)
    {
        switch (IsHubConnected)
        {
            case true when State == LightningNodeState.WaitingForConnection:
                State = LightningNodeState.Loading;
                break;
            case true when State is LightningNodeState.Loading or LightningNodeState.Loaded:
                _ = StopNode();
                break;
        }
    }

    private async Task OnChainWalletManagerOnStateChanged(object? sender, (OnChainWalletState Old, OnChainWalletState New) e)
    {
        if (e.New == OnChainWalletState.Loaded)
        {
            State = LightningNodeState.Loading;
        }
        else
        {
            _ = StopNode();
        }
    }

    private async Task OnStateChanged(object? sender, (LightningNodeState Old, LightningNodeState New) state)
    {
        LightningNodeState? newState = null;
        try
        {
            switch (state.New)
            {
                case LightningNodeState.WaitingForConnection:
                {
                    if (IsHubConnected)
                        newState = LightningNodeState.Loading;
                    break;
                }
                case LightningNodeState.Loading:
                    if (!IsHubConnected)
                    {
                        newState = LightningNodeState.WaitingForConnection;
                        break;
                    }

                    if (!IsOnchainConfigured || !IsOnchainLightningDerivationConfigured)
                    {
                        newState = LightningNodeState.NotConfigured;
                        break;
                    }
                    await StartNode();
                    break;

                case LightningNodeState.NotConfigured:
                    if (CanConfigureLightningNode)
                    {
                        await Generate();
                    }
                    break;

                case LightningNodeState.Loaded:
                    await _controlSemaphore.WaitAsync();

                    _controlSemaphore.Release();
                    break;
                case LightningNodeState.Stopped:
                case LightningNodeState.Error:
                    if (IsHubConnected)
                    {
                        await _btcPayConnectionManager.SwitchToSlave();
                        
                    }
                    break;
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
        StateChanged += OnStateChanged;
        _btcPayConnectionManager.ConnectionChanged += OnConnectionChanged;
        _onChainWalletManager.StateChanged += OnChainWalletManagerOnStateChanged;
    }

    protected override async Task ExecuteStopAsync(CancellationToken cancellationToken)
    {
        _btcPayConnectionManager.ConnectionChanged -= OnConnectionChanged;
        _onChainWalletManager.StateChanged += OnChainWalletManagerOnStateChanged;
        StateChanged -= OnStateChanged;
        _nodeScope?.Dispose();
    }
}
