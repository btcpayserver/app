using System.Data;
using BTCPayApp.CommonServer;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBitcoin.Scripting;

namespace BTCPayApp.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Setting> Settings { get; set; }
//     public DbSet<LightningChannel> LightningChannels { get; set; }
//     public DbSet<OnchainCoin> OnchainCoins { get; set; }
//     public DbSet<OnchainScript> OnchainScripts { get; set; }
//     public List<OnChainTransaction> OnChainTransactions { get; set; }
//     public List<LightningTransaction> LightningTransactions { get; set; }
// }
}

public class WalletConfig
{
    public const string Key = "walletconfig";
    
    public required string Mnemonic { get; set; }
    //key is the identifier of the tracker, value is a sub wallet format. 
    //for example, we will track native segwit wallet, the descriptor will be wpkh([fingerprint/84'/0'/0']xpub/0/*)
    // or for LN specifics, the descriptor is null, and we track non deterministic scripts
    public Dictionary<string, WalletDerivation> Derivations { get; set; } = new();

}

public class WalletDerivation
{
    public string Identifier { get; set; }
    public string Name { get; set; }
    public string? Descriptor { get; set; }

    public const string NativeSegwit = "Segwit";
    public const string LightningScripts = "LightningScripts";
}




public abstract class WalletService:IHostedService
{
    private readonly AppConfig _appConfig;
    private readonly BTCPayConnection _btcPayConnection;
    private WalletConfig _walletConfig;

    public WalletService(AppConfig appConfig, BTCPayConnection btcPayConnection)
    {
        _appConfig = appConfig;
        _btcPayConnection = btcPayConnection;
    }
    public async Task<WalletConfig> GenerateWallet()
    {
        await _started.Task;
        if (_btcPayConnection.Connection?.State !=  HubConnectionState.Connected)
            throw new InvalidOperationException("BTCPay not connected");
        if(_walletConfig != null)
            throw new InvalidOperationException("Wallet already generated");
        var newSeed = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var walletConfig = new WalletConfig
        {
            Mnemonic = newSeed.ToString()
        };
        var fingerPrint = newSeed.DeriveExtKey().GetPublicKey().GetHDFingerPrint();
        
        
        


    }


    public async Task StartListening(Mnemonic mnemonic, Network network)
    {
        var mainnet = network == Network.Main;
        var path = new KeyPath($"m/84'/{(mainnet? "0":"1")}'/0'");
        var fingerprint = mnemonic.DeriveExtKey().GetPublicKey().GetHDFingerPrint();
        var xpub = mnemonic.DeriveExtKey().Derive(path).Neuter().ToString(network);
        
        var segwitDerivation = new WalletDerivation
        {
            Name = "Native Segwit",
            Descriptor = OutputDescriptor.AddChecksum($"wpkh([{path.ToString().Replace("m", fingerprint.ToString())}]{xpub}/0/*)")
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
            if(pair.Key == WalletDerivation.NativeSegwit)
            {
                segwitDerivation.Identifier = pair.Value;
            }
            else if(pair.Key == WalletDerivation.LightningScripts)
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

    }


    private readonly TaskCompletionSource _started = new();
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _started.TrySetResult();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

public class AppConfig
{
    public string Network { get; set; }
}




