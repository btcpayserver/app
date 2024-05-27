using BTCPayApp.CommonServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Scripting;
using org.ldk.structs;
using org.ldk.util;
using OutPoint = NBitcoin.OutPoint;
using TxOut = NBitcoin.TxOut;

namespace BTCPayApp.Core.Attempt2;
public class OnChainWalletManager : BaseHostedService
{
    private readonly IConfigProvider _configProvider;
    private readonly BTCPayAppServerClient _btcPayAppServerClient;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly ILogger<OnChainWalletManager> _logger;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IMemoryCache _memoryCache;
    private OnChainWalletState _state = OnChainWalletState.Init;

    public WalletConfig? WalletConfig { get; private set; }
    public Network? Network => WalletConfig is null ? null : Network.GetNetwork(WalletConfig.Network);

    public OnChainWalletState State
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

    public event AsyncEventHandler<(OnChainWalletState Old, OnChainWalletState New)>? StateChanged;

    public OnChainWalletManager(
        IConfigProvider configProvider,
        BTCPayAppServerClient btcPayAppServerClient,
        BTCPayConnectionManager btcPayConnectionManager,
        ILogger<OnChainWalletManager> logger,
        IDbContextFactory<AppDbContext> dbContextFactory,
        IMemoryCache memoryCache)
    {
        _configProvider = configProvider;
        _btcPayAppServerClient = btcPayAppServerClient;
        _btcPayConnectionManager = btcPayConnectionManager;
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _memoryCache = memoryCache;
    }

    protected override async Task ExecuteStartAsync(CancellationToken cancellationToken)
    {
        State = OnChainWalletState.Init;
        StateChanged += OnStateChanged;
        _btcPayAppServerClient.OnNewBlock += OnNewBlock;
        _btcPayAppServerClient.OnTransactionDetected += OnTransactionDetected;
        _btcPayConnectionManager.ConnectionChanged += ConnectionChanged;
        WalletConfig = await _configProvider.Get<WalletConfig>(WalletConfig.Key);
        if (!IsConfigured)
        {
            State = OnChainWalletState.NotConfigured;
        }
        else if (IsHubConnected)
        {
            await Track();
            State = OnChainWalletState.Loaded;
        }
        else
        {
            State = OnChainWalletState.WaitingForConnection;
        }
    }

    private bool IsHubConnected => _btcPayConnectionManager.Connection?.State is HubConnectionState.Connected;
    private bool IsConfigured => WalletConfig is not null;

    private async Task OnStateChanged(object? sender, (OnChainWalletState Old, OnChainWalletState New) e)
    {
        if (e is {New: OnChainWalletState.Loaded} && IsConfigured)
        {
            await Track();
        }

        if (e.New is OnChainWalletState.Loading)
        {
            DetermineState();
        }
        
    }

    public async Task Generate()
    {
        await _controlSemaphore.WaitAsync();
        try
        {
            if (State != OnChainWalletState.NotConfigured || IsConfigured || !IsHubConnected)
            {
                throw new InvalidOperationException("Cannot generate wallet in current state");
            }

            var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
            var mainnet = _btcPayConnectionManager.ReportedNetwork == Network.Main;
            var path = new KeyPath($"m/84'/{(mainnet ? "0" : "1")}'/0'");
            var fingerprint = mnemonic.DeriveExtKey().GetPublicKey().GetHDFingerPrint();
            var xpub = mnemonic.DeriveExtKey().Derive(path).Neuter().ToString(_btcPayConnectionManager.ReportedNetwork);
            var walletConfig = new WalletConfig
            {
                Mnemonic = mnemonic.ToString(),
                Network = _btcPayConnectionManager.ReportedNetwork.ToString(),
                Derivations = new Dictionary<string, WalletDerivation>()
                {
                    [WalletDerivation.NativeSegwit] = new WalletDerivation()
                    {
                        Name = "Native Segwit",
                        Descriptor = OutputDescriptor.AddChecksum(
                            $"wpkh([{fingerprint.ToString()}/{path}]{xpub}/0/*)")
                    }
                }
            };

            var result = await _btcPayConnectionManager.HubProxy.Pair(new PairRequest()
            {
                Derivations = walletConfig.Derivations.ToDictionary(pair => pair.Key, pair => pair.Value.Descriptor)
            });
            foreach (var keyValuePair in result)
            {
                walletConfig.Derivations[keyValuePair.Key].Identifier = keyValuePair.Value;

            }
            await _configProvider.Set(WalletConfig.Key, walletConfig);
            WalletConfig = walletConfig;
            State = OnChainWalletState.Loaded;
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }

    public async Task AddDerivation(string key, string name, string? descriptor)
    {
        await _controlSemaphore.WaitAsync();
        try
        {
            if (State != OnChainWalletState.Loaded || !IsConfigured || !IsHubConnected)
            {
                throw new InvalidOperationException("Cannot add deriv in current state");
            }
            if (WalletConfig.Derivations.ContainsKey(key))
                throw new InvalidOperationException("Derivation already exists");

            var result = await _btcPayConnectionManager.HubProxy.Pair(new PairRequest
            {
                Derivations = new Dictionary<string, string?>()
                {
                    [key] = descriptor
                }
            });
            WalletConfig.Derivations[key] = new WalletDerivation()
            {
                Name = name,
                Descriptor = descriptor,
                Identifier = result[key]
            };
            await _configProvider.Set(WalletConfig.Key, WalletConfig);
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }

    private async Task ConnectionChanged(object? sender, HubConnectionState hubConnectionState)
    {
        DetermineState();
    }


    private void DetermineState()
    {
        var result = OnChainWalletState.Loading;
        if (IsHubConnected && IsConfigured)
        {
            result = OnChainWalletState.Loaded;
        }else if (!IsHubConnected)
        {
            result = OnChainWalletState.WaitingForConnection;
        }else if (!IsConfigured)
        {
            result = OnChainWalletState.NotConfigured;
        }
        State = result;
    }

    private async Task Track()
    {
        if (!IsConfigured || !IsHubConnected)
            return;

        var identifiers = WalletConfig.Derivations.Select(pair => pair.Value.Identifier).ToArray();
        var response = await _btcPayConnectionManager.HubProxy.Handshake(new AppHandshake
        {
            Identifiers = identifiers
        });

        var missing =
            WalletConfig.Derivations.Where(pair => !response.IdentifiersAcknowledged.Contains(pair.Value.Identifier));

        if (missing.Any())
        {
            _logger.LogWarning("Some identifiers that we had asked for BtcPayServer to track were not confirmed as being listened to. Tracking will be incomplete and functionality will critically fail.");
        }
    }

    protected override async Task ExecuteStopAsync(CancellationToken cancellationToken)
    {
        _btcPayAppServerClient.OnNewBlock -= OnNewBlock;
        _btcPayAppServerClient.OnTransactionDetected -= OnTransactionDetected;
        _btcPayConnectionManager.ConnectionChanged -= ConnectionChanged;
        WalletConfig = null;
        State = OnChainWalletState.Init;
    }

    private async Task OnTransactionDetected(object? sender, TransactionDetectedRequest transactionDetectedRequest)
    {
    }

    private async Task OnNewBlock(object? sender, string e)
    {
    }

    public async Task<Script> DeriveScript(string derivation)
    {
        var identifier = WalletConfig?.Derivations[derivation].Identifier;
        var addr = await _btcPayConnectionManager.HubProxy.DeriveScript(identifier);
        return Script.FromHex(addr);
    }

    public async Task<byte[]?> SignTransaction(byte[] psbtBytes)
    {
       var psbt =  PSBT.Load(psbtBytes, Network);
       psbt =  await SignTransaction(psbt);
       return psbt?.ToBytes();
    }
    public async Task<PSBT?> SignTransaction(PSBT psbt)
    {
        var identifiers = WalletConfig.Derivations.Select(derivation => derivation.Value.Identifier).ToArray();
        var updated = await _btcPayConnectionManager.HubProxy.UpdatePsbt(identifiers, psbt.ToHex());
        psbt = PSBT.Parse(updated, Network);
        var rootKey =new Mnemonic(WalletConfig.Mnemonic).DeriveExtKey();
        foreach (var deriv in WalletConfig.Derivations.Values.Where(derivation => derivation.Descriptor is not null))
        {
            var data = deriv.Descriptor.ExtractFromDescriptor(Network);
            if(data is null)
                continue;
            var accKey = rootKey.Derive(data.Value.Item2);
            psbt = psbt.SignAll(data.Value.Item1.AsHDScriptPubKey(data.Value.Item3), accKey);
            if(psbt.TryFinalize(out _))
                break;
        }

        return psbt;
    }

    private static ICoin ToCoin(CoinResponse response)
    {
        var outpoint = OutPoint.Parse(response.Outpoint);
        var scriptPubKey = Script.FromHex(response.Script);
        var amount = Money.Coins(response.Value);
        return new Coin(outpoint, new TxOut(amount, scriptPubKey));
    }

    // // public class SpendableOutputDescriptorCoin : Coin,ISignableCoin
    // {
    //     public SpendableOutputDescriptorCoin(OutPoint fromOutpoint, TxOut fromTxOut, SpendableOutputDescriptor descriptor) : base(fromOutpoint, fromTxOut)
    //     {
    //         Descriptor = descriptor;
    //     }
    //
    //     public SpendableOutputDescriptor Descriptor { get;}
    //     public async Task<PSBT> Sign(PSBT psbt)
    //     {
    //
    //         UtilMethods.
    //         UtilMethods.SpendableOutputDescriptor_create_spendable_outputs_psbt(new SpendableOutputDescriptor[]{Descriptor}, )
    //         Descriptor.create_spendable_outpcreate_spendable_outputs_psbtuts_psbt
    //         switch (Descriptor)
    //         {
    //             case SpendableOutputDescriptor.SpendableOutputDescriptor_DelayedPaymentOutput spendableOutputDescriptorDelayedPaymentOutput:
    //                 spendableOutputDescriptorDelayedPaymentOutput.delayed_payment_output.
    //                 break;
    //             case SpendableOutputDescriptor.SpendableOutputDescriptor_StaticOutput spendableOutputDescriptorStaticOutput:
    //                 //ignore
    //                 break;
    //             case SpendableOutputDescriptor.SpendableOutputDescriptor_StaticPaymentOutput spendableOutputDescriptorStaticPaymentOutput:
    //                 spendableOutputDescriptorStaticPaymentOutput.static_payment_output.psb
    //                 break;
    //             default:
    //                 throw new ArgumentOutOfRangeException(nameof(Descriptor));
    //         }
    //     }
    // }

    public class CoinWithKey : Coin,ISignableCoin
    {
        public Key Key { get; }

        public CoinWithKey(OutPoint fromOutpoint, TxOut fromTxOut, Key key) : base(fromOutpoint, fromTxOut)
        {
            Key = key;
        }

        public async Task<PSBT> Sign(PSBT psbt)
        {
            return psbt.SignWithKeys(Key);
        }
    }

    public interface ISignableCoin : ICoin
    {
        Task<PSBT> Sign(PSBT psbt);
    }

    public async Task<TxResp[]> GetTransactions()
    {
        var identifiersWhichWeCanDeriveKeysFor = WalletConfig.Derivations.Values
            .Where(derivation => derivation.Descriptor is not null).Select(derivation => derivation.Identifier).ToArray();
        var res= await _btcPayConnectionManager.HubProxy.GetTransactions(identifiersWhichWeCanDeriveKeysFor);
        return res.SelectMany(pair => pair.Value).OrderByDescending(resp => resp.Timestamp).ToArray();
    }


    public async Task<IEnumerable<ICoin>> GetUTXOS()
    {
        var identifiers = WalletConfig.Derivations.Values.Select(derivation => derivation.Identifier).ToArray();
        var utxos = await _btcPayConnectionManager.HubProxy.GetUTXOs(identifiers);
        var identifiersWhichWeCanDeriveKeysFor = WalletConfig.Derivations.Values
            .Where(derivation => derivation.Descriptor is not null).Select(derivation => derivation.Identifier).ToArray();
        var result = new List<ICoin>();

        var utxosThatWeCanDeriveKeysFor = utxos.Where(utxo => identifiersWhichWeCanDeriveKeysFor.Contains(utxo.Identifier)).ToArray();
        foreach (var coin in utxosThatWeCanDeriveKeysFor)
        {
            var derivation =
                WalletConfig.Derivations.Values.First(derivation => derivation.Identifier == coin.Identifier);
            var data = derivation.Descriptor.ExtractFromDescriptor(Network);
            if (data is null)
                continue;
            var coinKeyPath = KeyPath.Parse(coin.Path);
            var key = new Mnemonic(WalletConfig.Mnemonic).DeriveExtKey().Derive(data.Value.Item2.KeyPath)
                .Derive(coinKeyPath).PrivateKey;
            var c = ToCoin(coin);


            result.Add(new CoinWithKey(c.Outpoint, c.TxOut, key));

        }
        // if (WalletConfig.Derivations.TryGetValue(WalletDerivation.SpendableOutputs, out var spendableOutputDerivation))
        // {
        //
        //     var spendableOutputUtxos  = utxos.Where(response => response.Identifier == spendableOutputDerivation.Identifier).ToArray();
        //     await using var context = await _dbContextFactory.CreateDbContextAsync();
        //     var scipts = spendableOutputUtxos.Select(response => response.Script).Distinct();
        //     var spendableCoins = await context.SpendableCoins.Where(coin => scipts.Contains(coin.Script)).ToListAsync();
        //
        //     foreach (var spendableOutputUtxo in spendableOutputUtxos)
        //     {
        //         var spendableCoin = spendableCoins.FirstOrDefault(coin => coin.Script == spendableOutputUtxo.Script);
        //         if (spendableCoin is null)
        //             continue;
        //         var coin = ToCoin(spendableOutputUtxo);
        //         var data = SpendableOutputDescriptor.read(spendableCoin.Data);
        //         if(data is Result_SpendableOutputDescriptorDecodeErrorZ.Result_SpendableOutputDescriptorDecodeErrorZ_OK ok)
        //             result.Add(new SpendableOutputDescriptorCoin(coin.Outpoint, coin.TxOut, ok.res));
        //     }
        // }

        return result;
    }


    public async Task<(NBitcoin.Transaction Tx, ICoin[] SpentCoins, NBitcoin.Script Change)> CreateTransaction(
        List<TxOut> txOuts, FeeRate? feeRate, List<Coin> explicitIns = null)
    {
        var availableCoins = (await GetUTXOS()).ToList();
        feeRate ??= new FeeRate(await _btcPayConnectionManager.HubProxy.GetFeeRate(1));
//TODO: do not hardcode this constant
        var changeScript = await DeriveScript(WalletDerivation.NativeSegwit);
        var txBuilder = Network
            .CreateTransactionBuilder()
            .SetChange(changeScript)
            .SendEstimatedFees(feeRate);

        txBuilder = txOuts.Aggregate(txBuilder, (current, c) => current.Send(c.ScriptPubKey, c.Value));
        txBuilder.SendAllRemainingToChange();

        NBitcoin.Transaction? tx;
        if (explicitIns?.Any() is true)
        {
            txBuilder.AddCoins(explicitIns.ToArray());
        }
        while (true)
        {
            try
            {
                tx = txBuilder.BuildTransaction(true);
                return (tx, txBuilder.FindSpentCoins(tx), changeScript);
            }
            catch (NotEnoughFundsException e)
            {
                if (!availableCoins.Any())
                    throw;
                var newCoin = availableCoins.First();
                //TODO: switch to nuilding a psbt and signing with the ISignableCoin interface
                if(newCoin is CoinWithKey newCoinWithKey)
                {
                    txBuilder.AddCoins(newCoin);
                    txBuilder.AddKeys(newCoinWithKey.Key);
                }
                availableCoins.Remove(newCoin);
            }
        }
    }

    public async Task RemoveDerivation(params string[] key)
    {
        await _controlSemaphore.WaitAsync();
        try
        {
            if (State != OnChainWalletState.Loaded || WalletConfig is null)
            {
                throw new InvalidOperationException("Cannot remove deriv in current state");
            }

            var updated = key.Aggregate(false, (current, k) => current || WalletConfig.Derivations.Remove(k));
            if (updated)
                await _configProvider.Set(WalletConfig.Key, WalletConfig);
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }

    public async Task BroadcastTransaction(Transaction valueTx, CancellationToken cancellationToken = default)
    {
        await _btcPayConnectionManager.HubProxy.BroadcastTransaction(valueTx.ToHex());
    }

    public async Task<FeeRate> GetFeeRate(int blockTarget)
    {
        try
        {
            return await _memoryCache.GetOrCreateAsync($"feerate_{blockTarget}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return new FeeRate(await _btcPayConnectionManager.HubProxy.GetFeeRate(blockTarget));
            });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error getting fee rate, using hardcoded 100");
            return new FeeRate(100m);
        }
    }
}

public enum OnChainWalletState
{
    Init,
    NotConfigured,
    WaitingForConnection,
    Loading,
    Loaded
}
