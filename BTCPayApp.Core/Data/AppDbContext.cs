using System.Data;
using System.Text.Json.Serialization;
using BTCPayApp.CommonServer;
using BTCPayApp.Core.Contracts;
using BTCPayServer.Lightning;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBitcoin.Scripting;
using org.ldk.structs;

namespace BTCPayApp.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Setting> Settings { get; set; }
    public DbSet<Channel> LightningChannels { get; set; }
//     public DbSet<OnchainCoin> OnchainCoins { get; set; }
//     public DbSet<OnchainScript> OnchainScripts { get; set; }
//     public List<OnChainTransaction> OnChainTransactions { get; set; }
//     public List<LightningTransaction> LightningTransactions { get; set; }
// }
}

public class Channel
{
    public string Id { get; set; }
    public string SettingKey { get; set; }
    public Setting Setting { get; set; }
}

public class WalletConfig
{
    public const string Key = "walletconfig";

    public required string Mnemonic { get; set; }

    //key is the identifier of the tracker, value is a sub wallet format. 
    //for example, we will track native segwit wallet, the descriptor will be wpkh([fingerprint/84'/0'/0']xpub/0/*)
    // or for LN specifics, the descriptor is null, and we track non deterministic scripts
    public Dictionary<string, WalletDerivation> Derivations { get; set; } = new();

    public string Fingerprint => new Mnemonic(Mnemonic).DeriveExtKey().GetPublicKey().GetHDFingerPrint().ToString();

    public string Alias { get; set; }
    public string Color { get; set; }

    [JsonIgnore]
    public byte[] RGB
    {
        get
        {
            if(string.IsNullOrEmpty(Color)){ return [0,0,0];}

            if (Color.StartsWith("#"))
            {
                var rgBint = Convert.ToInt32("FFD700", 16);
                var red = (byte)((rgBint >> 16) & 255);
                var green = (byte)((rgBint >> 8) & 255);
                var blue = (byte)(rgBint & 255);
                return [red, green, blue];
            }
            else
            {
                var parts = Color.Split(',');
                if (parts.Length != 3)
                {
                    return [0, 0, 0];
                }
                return [byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2])];
            }
        }
    }
}

public class WalletDerivation
{
    public string Identifier { get; set; }
    public string Name { get; set; }
    public string? Descriptor { get; set; }

    public const string NativeSegwit = "Segwit";
    public const string LightningScripts = "LightningScripts";
}

public enum LightningNodeState
{
    InitialState,
    NotConfigured,
    WaitingForConnection,
    Starting,
    Running,
    Stopping,
    NotRunning,
    Error
}

public class WalletService : IHostedService
{
    private readonly Network _network;
    private readonly BTCPayConnection _btcPayConnection;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IConfigProvider _configProvider;
    private WalletConfig? _walletConfig;

    public LightningNodeState State
    {
        get => _state;
        private set
        {
            if (_state == value)
                return;
            _state = value;
            OnStateChanged?.Invoke(this, value);
        }
    }

    public string? CurrentWallet => _walletConfig?.Fingerprint;

    public event EventHandler<LightningNodeState>? OnStateChanged;


    public WalletService(
        Network network,
        BTCPayConnection btcPayConnection,
        IDbContextFactory<AppDbContext> dbContextFactory,
        IConfigProvider configProvider)
    {
        _network = network;
        _btcPayConnection = btcPayConnection;
        _dbContextFactory = dbContextFactory;
        _configProvider = configProvider;
    }


    public async Task GenerateWallet()
    {
        await _started.Task;
        if (_btcPayConnection.Connection?.State != HubConnectionState.Connected)
            throw new InvalidOperationException("BTCPay not connected");
        if (State != LightningNodeState.NotConfigured)
            throw new InvalidOperationException("Wallet already generated");
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var mainnet = _network == Network.Main;
        var path = new KeyPath($"m/84'/{(mainnet ? "0" : "1")}'/0'");
        var fingerprint = mnemonic.DeriveExtKey().GetPublicKey().GetHDFingerPrint();
        var xpub = mnemonic.DeriveExtKey().Derive(path).Neuter().ToString(_network);

        var segwitDerivation = new WalletDerivation
        {
            Name = "Native Segwit",
            Descriptor =
                OutputDescriptor.AddChecksum(
                    $"wpkh([{path.ToString().Replace("m", fingerprint.ToString())}]{xpub}/0/*)")
        };

        var ln = new WalletDerivation
        {
            Name = "Lightning",
            Descriptor = null
        };
        var pairRequest = new PairRequest()
        {
            Derivations = new Dictionary<string, string?>
            {
                {WalletDerivation.NativeSegwit, segwitDerivation.Descriptor},
                {WalletDerivation.LightningScripts, ln.Descriptor}
            }
        };

        var response = await _btcPayConnection.HubProxy.Pair(pairRequest);
        foreach (var pair in response)
        {
            if (pair.Key == WalletDerivation.NativeSegwit)
            {
                segwitDerivation.Identifier = pair.Value;
            }
            else if (pair.Key == WalletDerivation.LightningScripts)
            {
                ln.Identifier = pair.Value;
            }
        }

        var walletConfig = new WalletConfig()
        {
            Mnemonic = mnemonic.ToString(),
            Derivations = new Dictionary<string, WalletDerivation>
            {
                {WalletDerivation.NativeSegwit, segwitDerivation},
                {WalletDerivation.LightningScripts, ln}
            }
        };
        await _configProvider.Set(WalletConfig.Key, walletConfig);
        _walletConfig = walletConfig;
        State = LightningNodeState.WaitingForConnection;
    }


    private readonly TaskCompletionSource _started = new();
    private LightningNodeState _state = LightningNodeState.InitialState;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        OnStateChanged += OnOnStateChanged;
        _btcPayConnection.ConnectionChanged += BtcPayConnectionOnConnectionChanged;
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);


        _walletConfig = await _configProvider.Get<WalletConfig>(WalletConfig.Key);

        if (_walletConfig is null)
        {
            State = LightningNodeState.NotConfigured;
        }
        else
        {
            State = LightningNodeState.WaitingForConnection;
        }

        _started.TrySetResult();
    }

    private void BtcPayConnectionOnConnectionChanged(object? sender, EventArgs e)
    {
        if (_btcPayConnection.Connection?.State == HubConnectionState.Connected && State == LightningNodeState.WaitingForConnection)
        {
                State = LightningNodeState.Starting;
        }
    }

    private void OnOnStateChanged(object? sender, LightningNodeState e)
    {
        if (e == LightningNodeState.WaitingForConnection)
        {
            if(_btcPayConnection.Connection.State == HubConnectionState.Connected)
                State = LightningNodeState.Starting;
        }
        if(e == LightningNodeState.Starting)
        {
            //TODO: create scope and start LDKNode
        }
    }


    public async Task StopAsync(CancellationToken cancellationToken)
    {
        OnStateChanged = null;
        _btcPayConnection.ConnectionChanged -= BtcPayConnectionOnConnectionChanged;
    }

    public async Task UpdateNetworkGraph(NetworkGraph networkGraph)
    {
        await _configProvider.Set("NetworkGraph", networkGraph.write());
    }

    public async Task UpdateScore(WriteableScore score)
    {
        await _configProvider.Set("Score", score.write());
    }
}