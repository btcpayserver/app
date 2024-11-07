using BTCPayApp.CommonServer;
using BTCPayApp.Core.Backup;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Scripting;
using OutPoint = NBitcoin.OutPoint;
using TxOut = NBitcoin.TxOut;

namespace BTCPayApp.Core.Wallet;
public class OnChainWalletManager : BaseHostedService
{
    public const string PaymentMethodId = "BTC-CHAIN";

    private readonly ConfigProvider _configProvider;
    private readonly BTCPayAppServerClient _btcPayAppServerClient;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly ILogger<OnChainWalletManager> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly SyncService _syncService;
    private OnChainWalletState _state = OnChainWalletState.Init;

    // public WalletConfig? WalletConfig { get; private set; }
    // public Task<WalletDerivation?> Derivation =>
    //     GetConfig().ContinueWith(task => task.Result?.Derivations.Values.FirstOrDefault());
    public Task<bool> IsConfigured() => GetConfig().ContinueWith(task => IsConfigured(task.Result));

    public bool IsConfigured(WalletConfig? walletConfig) =>
        walletConfig?.Derivations.TryGetValue(WalletDerivation.NativeSegwit, out _) is true;

    public bool CanConfigureWallet(WalletConfig walletConfig)
    {
        return IsHubConnected && !IsConfigured(walletConfig);
    }

    public async Task<bool> CanConfigureWallet()
    {
        return CanConfigureWallet(await GetConfig());
    }

    private bool IsHubConnected => _btcPayConnectionManager.ConnectionState is BTCPayConnectionState.ConnectedAsMaster;
    public bool IsActive => State == OnChainWalletState.Loaded;

    public OnChainWalletState State
    {
        get => _state;
        private set
        {
            if (_state == value)
                return;
            var old = _state;
            _state = value;
            _logger.LogInformation($"Wallet state changed: {_state} from {old}");
            StateChanged?.Invoke(this, (old, value));
        }
    }

    public event AsyncEventHandler<(OnChainWalletState Old, OnChainWalletState New)>? StateChanged;

    public OnChainWalletManager(
        ConfigProvider configProvider,
        BTCPayAppServerClient btcPayAppServerClient,
        BTCPayConnectionManager btcPayConnectionManager,
        ILogger<OnChainWalletManager> logger,
        IMemoryCache memoryCache,
        SyncService syncService) : base(logger)
    {
        _configProvider = configProvider;
        _btcPayAppServerClient = btcPayAppServerClient;
        _btcPayConnectionManager = btcPayConnectionManager;
        _logger = logger;
        _memoryCache = memoryCache;
        _syncService = syncService;
    }

    public async Task<WalletConfig?> GetConfig()
    {
        return await _configProvider.Get<WalletConfig?>(WalletConfig.Key);
    }

    protected override async Task ExecuteStartAsync(CancellationToken cancellationToken)
    {
        StateChanged += OnStateChanged;
        _btcPayAppServerClient.OnNewBlock += OnNewBlock;
        _btcPayAppServerClient.OnTransactionDetected += OnTransactionDetected;
        _btcPayConnectionManager.ConnectionChanged += ConnectionChanged;

        await DetermineState();
        if (IsHubConnected)
        {
            await Track();

            _ = GetBestBlock();
            State = OnChainWalletState.Loaded;
        }
    }

    private async Task OnStateChanged(object? sender, (OnChainWalletState Old, OnChainWalletState New) e)
    {
        var config = await GetConfig();
        if (e is {New: OnChainWalletState.Loaded} && IsConfigured(config))
        {
            await Track();
        }

        if (e.New is OnChainWalletState.Loading)
        {
            await DetermineState();
        }
    }

    public Task Restore()
    {
        throw new NotImplementedException("we're not there yet");
        /*
        await _controlSemaphore.WaitAsync();
        try
        {


            var config = await GetConfig();
            var missingIds = await Track();
            foreach (var missing in missingIds)
            {
                var wd = config.Derivations.First(pair => pair.Value.Identifier == missing).Value;
                if (wd.Descriptor is null)
                {
                    // track and take the new identifier

                }

                //import utxos for the missing id
                //ask ldk for the scipts we should be tacking and add them all
            }

            //if it is a wallet without a derivation, we generate a new goup for it
        }
        finally
        {
            _controlSemaphore.Release();
        }
        */
    }


    public async Task Generate()
    {
        await _controlSemaphore.WaitAsync();
        try
        {
            if (State != OnChainWalletState.NotConfigured || !IsHubConnected || IsConfigured(await GetConfig()) ||
                await GetBestBlock() is not { } block)
            {
                throw new InvalidOperationException("Cannot generate wallet in current state");
            }

            _logger.LogInformation("Generating wallet");

            var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
            var mainnet = _btcPayConnectionManager.ReportedNetwork == Network.Main;
            var path = new KeyPath($"m/84'/{(mainnet ? "0" : "1")}'/0'");
            var fingerprint = mnemonic.DeriveExtKey().GetPublicKey().GetHDFingerPrint();
            var xpub = mnemonic.DeriveExtKey().Derive(path).Neuter().ToString(_btcPayConnectionManager.ReportedNetwork);
            var snapshot = new BlockSnapshot()
            {
                BlockHash = uint256.Parse(block.BlockHash),
                BlockHeight = (uint) block.BlockHeight
            };
            var walletConfig = new WalletConfig
            {
                Birthday = snapshot,
                Mnemonic = mnemonic.ToString(),
                Network = _btcPayConnectionManager.ReportedNetwork.ToString(),
                Derivations = new Dictionary<string, WalletDerivation>()
                {
                    [WalletDerivation.NativeSegwit] = new()
                    {
                        Name = "Native Segwit",
                        Descriptor = OutputDescriptor.AddChecksum(
                            $"wpkh([{fingerprint.ToString()}/{path}]{xpub}/0/*)")
                    }
                },
                CoinSnapshot = new CoinSnapshot()
                {
                    Coins = new(),
                    BlockSnapshot = snapshot
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

            if (!await _syncService.SetEncryptionKey(mnemonic))
            {
                _logger.LogError("Failed to set encryption key");
                return;
            }

            ;
            await _configProvider.Set(WalletConfig.Key, walletConfig, true);
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
            var config = await GetConfig();
            if (State != OnChainWalletState.Loaded || !IsHubConnected || config is null || !IsConfigured(config))
            {
                throw new InvalidOperationException("Cannot add deriv in current state");
            }

            if (config.Derivations.ContainsKey(key))
                throw new InvalidOperationException("Derivation already exists");

            var result = await _btcPayConnectionManager.HubProxy.Pair(new PairRequest
            {
                Derivations = new Dictionary<string, string?>
                {
                    [key] = descriptor
                }
            });

            config.Derivations[key] = new WalletDerivation
            {
                Name = name,
                Descriptor = descriptor,
                Identifier = result[key]
            };
            await _configProvider.Set(WalletConfig.Key, config, true);
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }

    private async Task ConnectionChanged(object? sender,
        (BTCPayConnectionState Old, BTCPayConnectionState New) valueTuple)
    {
        // if (valueTuple.New is BTCPayConnectionState.ConnectedFinishedInitialSync)
        // {
        //     WalletConfig = await _configProvider.Get<WalletConfig>(WalletConfig.Key);
        // }
        await DetermineState();
    }

    private async Task DetermineState()
    {
        var config = await GetConfig();
        var configured = IsConfigured(config);
        switch (IsHubConnected)
        {
            case true when configured && config!.NBitcoinNetwork != _btcPayConnectionManager.ReportedNetwork:
                State = OnChainWalletState.Error;
                break;
            case true when configured:
                State = OnChainWalletState.Loaded;
                break;
            case false:
                State = OnChainWalletState.WaitingForConnection;
                break;
            default:
            {
                if (!configured)
                    State = OnChainWalletState.NotConfigured;
                break;
            }
        }
    }

    private async Task<string[]> Track()
    {
        if (!IsHubConnected)
            return [];
        var config = await GetConfig();
        if (config is null ||!IsConfigured(config))
        {
            return [];
        }

        var identifiers = config.Derivations.Select(pair => pair.Value.Identifier).ToArray();
        var response = await _btcPayConnectionManager.HubProxy.Handshake(new AppHandshake
        {
            Identifiers = identifiers
        });

        var missing = config.Derivations
            .Where(pair => response.IdentifiersAcknowledged?.Contains(pair.Value.Identifier) is not true)
            .ToList();

        if (missing.Count != 0)
        {
            _logger.LogWarning(
                "Some identifiers that we had asked for BtcPayServer to track were not confirmed as being listened to. Tracking will be incomplete and functionality will critically fail");
        }

        return missing.Select(pair => pair.Key).ToArray();

    }

    protected override Task ExecuteStopAsync(CancellationToken cancellationToken)
    {
        _btcPayAppServerClient.OnNewBlock -= OnNewBlock;
        _btcPayAppServerClient.OnTransactionDetected -= OnTransactionDetected;
        _btcPayConnectionManager.ConnectionChanged -= ConnectionChanged;
        State = OnChainWalletState.Init;
        return Task.CompletedTask;
    }

    private Task OnTransactionDetected(object? sender, TransactionDetectedRequest transactionDetectedRequest)
    {
        _ = UpdateSnapshot();
        return Task.CompletedTask;
    }

    private Task OnNewBlock(object? sender, string e)
    {
        _memoryCache.Remove("bestblock");
        _ = GetBestBlock();
        _ = UpdateSnapshot();
        return Task.CompletedTask;
    }

    private async Task UpdateSnapshot()
    {
        await _controlSemaphore.WaitAsync();
        try
        {
            var config = await GetConfig();
            var bb = await GetBestBlock();
            var identifiers = config.Derivations.Values.Select(derivation => derivation.Identifier).ToArray();
            var utxos = (await _btcPayConnectionManager.HubProxy.GetUTXOs(identifiers))
                .GroupBy(response => response.Identifier)
                .ToDictionary(grouping => grouping.Key,
                    grouping => grouping.Select(response => response.Outpoint).ToArray());
            config.CoinSnapshot = new CoinSnapshot()
            {
                BlockSnapshot = new BlockSnapshot()
                {
                    BlockHash = uint256.Parse(bb.BlockHash),
                    BlockHeight = (uint) bb.BlockHeight
                },
                Coins = utxos
            };
            await _configProvider.Set(WalletConfig.Key, config, true);
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }


    public async Task<BitcoinAddress> DeriveScript(string derivation)
    {
        var config = await GetConfig();
        var identifier = config?.Derivations[derivation].Identifier;
        var addr = await _btcPayConnectionManager.HubProxy.DeriveScript(identifier);
        return Script.FromHex(addr).GetDestinationAddress(_btcPayConnectionManager.ReportedNetwork);
    }

    public async Task<PSBT?> SignTransaction(byte[] psbtBytes)
    {
        var psbt = PSBT.Load(psbtBytes, _btcPayConnectionManager.ReportedNetwork);
        psbt = await SignTransaction(psbt);
        return psbt;
    }

    public async Task<PSBT?> SignTransaction(PSBT psbt)
    {
        var config = await GetConfig();
        var identifiers = config.Derivations.Select(derivation => derivation.Value.Identifier).ToArray();
        var updated = await _btcPayConnectionManager.HubProxy.UpdatePsbt(identifiers, psbt.ToHex());
        psbt = PSBT.Parse(updated, config.NBitcoinNetwork);
        var rootKey = new Mnemonic(config.Mnemonic).DeriveExtKey();
        foreach (var deriv in config.Derivations.Values.Where(derivation => derivation.Descriptor is not null))
        {
            var data = deriv.Descriptor.ExtractFromDescriptor(config.NBitcoinNetwork);
            if (data is null)
                continue;
            var accKey = rootKey.Derive(data.Value.Item2);
            psbt = psbt.SignAll(data.Value.Item1.AsHDScriptPubKey(data.Value.Item3), accKey);
            if (psbt.TryFinalize(out _))
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

    public class CoinWithKey : Coin, ISignableCoin
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

    public async Task<Dictionary<string, TxResp[]>> GetTransactions()
    {
        var config = await GetConfig();
        var identifiersWhichWeCanDeriveKeysFor = config.Derivations.Values
            .Select(derivation => derivation.Identifier).ToArray();
        var res = await _btcPayConnectionManager.HubProxy.GetTransactions(identifiersWhichWeCanDeriveKeysFor);
        return res.ToDictionary(
            pair =>
            {
                return config.Derivations.First(derivation =>
                    derivation.Value.Identifier.Equals(pair.Key, StringComparison.InvariantCultureIgnoreCase)).Key;
            }, pair => pair.Value.OrderByDescending(tx => tx.Timestamp).ToArray());
    }


    public async Task<IEnumerable<ICoin>> GetUTXOS()
    {
        var config = await GetConfig();
        var identifiers = config.Derivations.Values.Select(derivation => derivation.Identifier).ToArray();
        var utxos = await _btcPayConnectionManager.HubProxy.GetUTXOs(identifiers);
        var identifiersWhichWeCanDeriveKeysFor = config.Derivations.Values
            .Where(derivation => derivation.Descriptor is not null).Select(derivation => derivation.Identifier)
            .ToArray();
        var result = new List<ICoin>();

        var utxosThatWeCanDeriveKeysFor =
            utxos.Where(utxo => identifiersWhichWeCanDeriveKeysFor.Contains(utxo.Identifier)).ToArray();
        foreach (var coin in utxosThatWeCanDeriveKeysFor)
        {
            var derivation =
                config.Derivations.Values.First(derivation => derivation.Identifier == coin.Identifier);
            var data = derivation.Descriptor.ExtractFromDescriptor(config.NBitcoinNetwork);
            if (data is null)
                continue;
            var coinKeyPath = KeyPath.Parse(coin.Path);
            var key = new Mnemonic(config.Mnemonic).DeriveExtKey().Derive(data.Value.Item2.KeyPath)
                .Derive(coinKeyPath).PrivateKey;
            var c = ToCoin(coin);


            result.Add(new CoinWithKey(c.Outpoint, c.TxOut, key));
        }

        foreach (var coin in utxos.Where(utxo => !identifiersWhichWeCanDeriveKeysFor.Contains(utxo.Identifier)))
        {
            result.Add(ToCoin(coin));
        }

        return result;
    }


    public async Task<(NBitcoin.Transaction Tx, ICoin[] SpentCoins, BitcoinAddress Change)> CreateTransaction(
        List<TxOut> txOuts, FeeRate? feeRate, List<Coin> explicitIns = null)
    {
        var availableCoins = (await GetUTXOS()).OfType<CoinWithKey>().ToList();
        feeRate ??= await GetFeeRate(1);

        var config = await GetConfig();
//TODO: do not hardcode this constant
        var changeScript = await DeriveScript(WalletDerivation.NativeSegwit);
        var txBuilder = config.NBitcoinNetwork
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
                if (newCoin is CoinWithKey newCoinWithKey)
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
            var config = await GetConfig();
            if (State != OnChainWalletState.Loaded || config is null)
            {
                throw new InvalidOperationException("Cannot remove deriv in current state");
            }

            var updated = key.Aggregate(false, (current, k) => current || config.Derivations.Remove(k));
            if (updated)
                await _configProvider.Set(WalletConfig.Key, config, true);
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }

    public async Task<BestBlockResponse?> GetBestBlock()
    {
        var res = await _memoryCache.GetOrCreateAsync("bestblock", async entry =>
        {
            _logger.LogInformation("Getting best block");
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            try
            {
                return await _btcPayConnectionManager.HubProxy.GetBestBlock();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error getting best block");
                return null;
            }
            finally
            {
                _logger.LogInformation("Got best block");
            }
        });
        if (res is null)
        {
            _memoryCache.Remove("bestblock");
        }

        return res;
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
                _logger.LogInformation("Getting fee rate for block target {BlockTarget}", blockTarget);

                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

                var result = new FeeRate(await _btcPayConnectionManager.HubProxy.GetFeeRate(blockTarget));

                _logger.LogInformation($"Got fee rate for block target {blockTarget} {result}");
                return result;
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
    Loaded,
    Error
}
