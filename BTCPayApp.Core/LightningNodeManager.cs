using BTCPayApp.Core.Contracts;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using uniffi.ldk_node;
using Network = uniffi.ldk_node.Network;

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
    private readonly IDataDirectoryProvider _directoryProvider;
    private LightningNodeState _state = LightningNodeState.NotConfigured;
    private ILdkNode? Node { get; set; }
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
        _directoryProvider = directoryProvider;
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
        if (Node is not null && RunningNodeId == internalId) return true;

        // Handle existing node
        if (Node is not null) StopNode();

        // Run the new node
        try
        {
            var builder = await BuilderForMnemonic(mnemonic);
            Node = builder.Build();
            State = LightningNodeState.Starting;
            RunningNodeId = internalId;
            Node.Start();
            State = LightningNodeState.Running;
            return true;
        }
        catch (Exception)
        {
            State = LightningNodeState.Error;
            return false;
        }
    }

    public bool StopNode()
    {
        if (State is LightningNodeState.NotRunning || Node is null) return true;
        try
        {
            State = LightningNodeState.Stopping;
            Node.Stop();
        }
        catch (NodeException.NotRunning)
        {
            // ok
        }
        finally
        {
            Node = null;
            RunningNodeId = null;
            State = LightningNodeState.NotRunning;
        }
        return true;
    }

    private static string InternalNodeIdForMnemonic(Mnemonic mnemonic)
    {
        var kp = new KeyPath("m/84'/0'/0'");
        var extKey = mnemonic.DeriveExtKey();
        var derived = extKey.Derive(kp);
        return derived.Neuter().ParentFingerprint.ToString()!;
    }

    private async Task<Builder> BuilderForMnemonic(Mnemonic mnemonic)
    {
        var internalId = InternalNodeIdForMnemonic(mnemonic);
        var storageDir = await _directoryProvider.GetAppDataDirectory().ContinueWith(task =>
        {
            var res =  Path.Combine(task.Result, "nodes", internalId);
            Directory.CreateDirectory(res);
            return res;
        });

        var builder = new Builder();
        builder.SetNetwork(Network.TESTNET);
        builder.SetEsploraServer("https://blockstream.info/testnet/api");
        builder.SetGossipSourceRgs("https://rapidsync.lightningdevkit.org/testnet/snapshot");
        builder.SetStorageDirPath(storageDir);
        builder.SetEntropyBip39Mnemonic(mnemonic.ToString()!, null);
        return builder;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopNode();
        return Task.CompletedTask;
    }
}
