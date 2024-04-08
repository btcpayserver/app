using BTCPayApp.Core.Contracts;
using Microsoft.Extensions.Hosting;
using NBitcoin;

namespace BTCPayApp.Core;

public enum LightningNodeState
{
    NotConfigured,
    Starting,
    Running,
    Stopping,
    NotRunning,
    Error
}

public class LightningNodeManager : IHostedService
{
    private readonly BTCPayAppConfigManager _configManager;
    private LightningNodeState _state = LightningNodeState.NotConfigured;
    private string? RunningNodeId { get; set; }
    public event EventHandler<LightningNodeState>? StateChanged;

    public LightningNodeState State
    {
        get => _state;
        private set
        {
            var update = _state != value;
            _state = value;
            if (update)
            {
                StateChanged?.Invoke(this, value);
            }
        }
    }

    public LightningNodeManager(
        BTCPayAppConfigManager configManager,
        IDataDirectoryProvider directoryProvider)
    {
        _configManager = configManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _configManager.Loaded.Task.ContinueWith(async _ =>
        {
            if (_configManager.WalletConfig is not null)
            {
                await RunNode(_configManager.WalletConfig);
            }
        }, cancellationToken);
    }

    public async Task<bool> RunNode(WalletConfig config)
    {
        var mnemonic = new Mnemonic(config.Mnemonic);
        var internalId = InternalNodeIdForMnemonic(mnemonic);

        // Run the new node
        try
        {
            State = LightningNodeState.Starting;
            RunningNodeId = internalId;
            State = LightningNodeState.Running;
            return true;
        }
        catch (Exception e)
        {
            State = LightningNodeState.Error;
            return false;
        }
    }

    public bool StopNode()
    {
        if (State is LightningNodeState.NotRunning) return true;

        State = LightningNodeState.Stopping;


        RunningNodeId = null;
        State = LightningNodeState.NotRunning;
        return true;
    }

    private static string InternalNodeIdForMnemonic(Mnemonic mnemonic)
    {
        var kp = new KeyPath("m/84'/0'/0'");
        var extKey = mnemonic.DeriveExtKey();
        var derived = extKey.Derive(kp);
        return derived.Neuter().ParentFingerprint.ToString()!;
    }


    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopNode();
        return Task.CompletedTask;
    }
}