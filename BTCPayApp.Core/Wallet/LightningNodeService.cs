using System.Linq.Expressions;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.Wallet;

public class LightningNodeManager(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ILogger<LightningNodeManager> logger,
    OnChainWalletManager onChainWalletManager,
    BTCPayConnectionManager btcPayConnectionManager,
    IServiceScopeFactory serviceScopeFactory)
    : BaseHostedService(logger)
{
    public const string PaymentMethodId = "BTC-LN";
    private IServiceScope? _nodeScope;
    private LightningNodeState _state = LightningNodeState.Init;
    public bool IsActive => State == LightningNodeState.Loaded;
    public bool IsHubConnected => btcPayConnectionManager.ConnectionState is BTCPayConnectionState.ConnectedAsPrimary;
    private async Task<bool> IsOnchainLightningDerivationConfigured () => (await onChainWalletManager.GetConfig())?.Derivations.ContainsKey(WalletDerivation.LightningScripts) is true;
    public async Task<bool> CanConfigureLightningNode () => IsHubConnected && await onChainWalletManager.IsConfigured() && !await IsOnchainLightningDerivationConfigured() && State == LightningNodeState.NotConfigured;
    public event AsyncEventHandler<(LightningNodeState Old, LightningNodeState New)>? StateChanged;
    public LDKNode? Node
    {
        get
        {
            try
            {
                return _nodeScope?.ServiceProvider.GetService<LDKNode>();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while getting LDKNode");
                return null;
            }
        }
    }

    public LightningNodeState State
    {
        get => _state;
        private set
        {
            if (_state == value)
                return;
            var old = _state;
            _state = value;
            logger.LogInformation("Lightning node state changed: {Old} -> {State}", old, _state);
            StateChanged?.Invoke(this, (old, value));
        }
    }

    public async Task StartNode()
    {
        if (_nodeScope is not null || State is LightningNodeState.Loaded or LightningNodeState.NotConfigured)
            return;

        if (onChainWalletManager.State is not OnChainWalletState.Loaded)
        {
            State = LightningNodeState.WaitingForConnection;
            return;
        }

        logger.LogInformation("Starting lightning node");
        await ControlSemaphore.WaitAsync();
        try
        {
            if (_nodeScope is null)
            {
                _nodeScope = serviceScopeFactory.CreateScope();
                CancellationTokenSource = new CancellationTokenSource();
            }
            await Node!.StartAsync(CancellationTokenSource.Token);

            State = LightningNodeState.Loaded;
        }
        catch (Exception e)
        {
            _nodeScope?.Dispose();
            logger.LogError(e, "Error while starting lightning node");
            _nodeScope = null;
            State = LightningNodeState.Error;
        }
        finally
        {
            ControlSemaphore.Release();
        }
    }

    public async Task StopNode() => await StopNode(true);

    private async Task StopNode(bool setAsStopped = true)
    {
        if (_nodeScope is null)
            return;
        await ControlSemaphore.WaitAsync();
        try
        {
            logger.LogInformation("Stopping lightning node");
            if (Node != null) await Node.StopAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while stopping lightning node");
        }
        finally
        {
            _nodeScope?.Dispose();
            _nodeScope = null;
            ControlSemaphore.Release();
            if (setAsStopped)
                State = LightningNodeState.Stopped;
        }
    }

    public async Task CleanseTask()
    {
        await StopNode();

        if (_nodeScope is not null || State == LightningNodeState.NotConfigured) return;

        await ControlSemaphore.WaitAsync();
        try
        {
            await onChainWalletManager.RemoveDerivation(WalletDerivation.LightningScripts);
            await using var context = await dbContextFactory.CreateDbContextAsync();
            context.LightningPayments.RemoveRange(context.LightningPayments);
            // context.OutboxItems.RemoveRange(context.OutboxItems);
            context.Settings.RemoveRange(context.Settings.Where(s => s.Key.StartsWith("ln:")));
            await context.SaveChangesAsync();
        }
        finally
        {
            ControlSemaphore.Release();

            State = LightningNodeState.NotConfigured;
        }
    }

    public async Task Generate()
    {
        await ControlSemaphore.WaitAsync();
        try
        {
            if (!IsHubConnected)
                throw new InvalidOperationException("Cannot configure lightning node without BTCPay connection");

            if (!await onChainWalletManager.IsConfigured())
                throw new InvalidOperationException("Cannot configure lightning node without on-chain wallet configuration");
            if (await IsOnchainLightningDerivationConfigured())
                throw new InvalidOperationException("On-chain wallet is already configured with a lightning derivation");

            logger.LogInformation("Generating lightning node");
            await onChainWalletManager.AddDerivation(WalletDerivation.LightningScripts, "Lightning", null);
            // await _onChainWalletManager.AddDerivation(WalletDerivation.SpendableOutputs, "Lightning Spendables", null);
            State = LightningNodeState.WaitingForConnection;
        }
        finally
        {
            ControlSemaphore.Release();
        }
    }

    private Task OnConnectionChanged(object? sender, (BTCPayConnectionState Old, BTCPayConnectionState New) valueTuple)
    {
        State = IsHubConnected switch
        {
            true when State == LightningNodeState.WaitingForConnection => LightningNodeState.Loading,
            false => LightningNodeState.Loading,
            _ => State
        };
        return Task.CompletedTask;
    }

    private Task OnChainWalletManagerOnStateChanged(object? sender, (OnChainWalletState Old, OnChainWalletState New) e)
    {
        if (e.New == OnChainWalletState.Loaded)
        {
            State = LightningNodeState.Loading;
        }
        else
        {
            _ = StopNode();
        }
        return Task.CompletedTask;
    }

    private async Task OnStateChanged(object? sender, (LightningNodeState Old, LightningNodeState New) state)
    {
        LightningNodeState? newState = null;
        try
        {
            switch (state.New)
            {
                case LightningNodeState.Init:
                    newState = LightningNodeState.WaitingForConnection;
                    break;

                case LightningNodeState.WaitingForConnection:
                    if (IsHubConnected)
                        newState = LightningNodeState.Loading;
                    break;

                case LightningNodeState.Loading:
                    await StopNode(false);
                    if (!IsHubConnected)
                    {
                        newState = LightningNodeState.WaitingForConnection;
                        break;
                    }
                    if (!await onChainWalletManager.IsConfigured() || !await IsOnchainLightningDerivationConfigured())
                    {
                        newState = LightningNodeState.NotConfigured;
                        break;
                    }
                    await StartNode();
                    break;

                case LightningNodeState.NotConfigured:
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
        await StateChanged.Invoke(this, (LightningNodeState.Init, LightningNodeState.Init));
        // _btcPayConnectionManager.ConnectionChanged += OnConnectionChanged;
        onChainWalletManager.StateChanged += OnChainWalletManagerOnStateChanged;
    }

    protected override Task ExecuteStopAsync(CancellationToken cancellationToken)
    {
        // _btcPayConnectionManager.ConnectionChanged -= OnConnectionChanged;
        onChainWalletManager.StateChanged += OnChainWalletManagerOnStateChanged;
        StateChanged -= OnStateChanged;
        _nodeScope?.Dispose();
        return Task.CompletedTask;
    }
}
