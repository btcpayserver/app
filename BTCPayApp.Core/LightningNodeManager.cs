using System.Data;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using NBitcoin;

namespace BTCPayApp.Core;

public enum LightningNodeState 
{
    NotConfigured,
    Bootstrapping,
    Starting,
    WaitingForBackend,
    Connected,
    ShuttingDown,
    Error
}
public class LightningNodeManager: IHostedService
{
    
    private readonly BTCPayConnection _connection;
    private readonly BTCPayAppConfigManager _configManager;
    private LightningNodeState _state = LightningNodeState.NotConfigured;
    private ExtKey? NodeKey { get; set; }

    public event EventHandler<LightningNodeState>? StateChanged;
    public LightningNodeState State
    {
        get => _state;
        private set
        {
            bool update = _state != value;
            _state = value;
            if (update)
            {
                StateChanged?.Invoke(this, value);
            }
        }
    }


    public LightningNodeManager(
        BTCPayConnection connection, 
        BTCPayAppConfigManager configManager)
    {
        _connection = connection;
        _configManager = configManager;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _configManager.WalletConfigUpdated += OnWalletConfigUpdated;
        
        _configManager.Loaded.Task.ContinueWith(_ =>
        {
            if (_configManager.WalletConfig is not null)
            {
                OnWalletConfigUpdated(this, _configManager.WalletConfig);
            }
        }, cancellationToken);
        return Task.CompletedTask;
    }

    private void OnWalletConfigUpdated(object? sender, WalletConfig? e)
    {
        if (e is null && State == LightningNodeState.NotConfigured)
        {
            return;
        }

        if (e is null)
        {
            _ = KillNode();
            return;
        }
        var newMnemonic = new Mnemonic(e.Mnemonic).DeriveExtKey().Derive(new KeyPath(e.DerivationPath));
        if (NodeKey is not null && newMnemonic.Equals(NodeKey)) 
        {
            return;
        }

        _ =  ReplaceNode(NodeKey);



    }

    private async Task ReplaceNode(ExtKey nodeKey)
    {
        await KillNode();
        State = LightningNodeState.Bootstrapping;
        await Task.Delay(3000);
        NodeKey = nodeKey;
        
        State = LightningNodeState.WaitingForBackend;
        while(_configManager.WalletConfig?.StandaloneMode is true && _connection.Connection?.State is not HubConnectionState.Connected)
        {
            await Task.Delay(500);
        }
        State = LightningNodeState.Starting;
        await Task.Delay(3000); 
        State = LightningNodeState.Connected;
        
    }

    private async Task KillNode()
    {
        if(State is LightningNodeState.NotConfigured or LightningNodeState.Error)
            return;
        State = LightningNodeState.ShuttingDown;
        await Task.Delay(3000);
    }


    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await KillNode();
    }
}