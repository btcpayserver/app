using System.Text.Json.Serialization;
using BTCPayApp.Core.Backup;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
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

    private IBTCPayAppHubServer? HubProxy => _btcPayConnectionManager.HubProxy;
    private Network? ReportedNetwork => _btcPayConnectionManager.ReportedNetwork;

    // public WalletConfig? WalletConfig { get; private set; }
    // public Task<WalletDerivation?> Derivation =>
    //     GetConfig().ContinueWith(task => task.Result?.Derivations.Values.FirstOrDefault());
    public Task<bool> IsConfigured() => GetConfig().ContinueWith(task => IsConfigured(task.Result));

    public static bool IsConfigured(WalletConfig? walletConfig) =>
        walletConfig?.Derivations.TryGetValue(WalletDerivation.NativeSegwit, out _) is true;

    private bool CanConfigureWallet(WalletConfig? walletConfig)
    {
        return IsHubConnected && !IsConfigured(walletConfig);
    }

    public async Task<bool> CanConfigureWallet()
    {
        return CanConfigureWallet(await GetConfig());
    }

    public bool IsHubConnected => _btcPayConnectionManager.ConnectionState is BTCPayConnectionState.ConnectedAsPrimary;
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
            _logger.LogInformation("Wallet state changed: {Old} -> {State}", old, _state);
            StateChanged?.Invoke(this, (old, value));
        }
    }

    public event AsyncEventHandler<(OnChainWalletState Old, OnChainWalletState New)>? StateChanged;
    public event AsyncEventHandler<CoinSnapshot>? OnSnapshotUpdate;

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

    public async Task Restore()
    {
        try
        {
            if (_state != OnChainWalletState.WaitingForConnection)
                throw new InvalidOperationException("Cannot restore wallet in current state");

            var config = await GetConfig();
            if (config is null || !IsConfigured(config) || HubProxy == null)
                throw new InvalidOperationException("Cannot restore wallet in current state");

            await ControlSemaphore.WaitAsync();

            // step1: Track our derivations
            //for groups, we need to generate a new one and replace the local one with its identifier
            //for derivations, we need to call track with the latest index
            //for groups, we need to fetch all tracked scripts and add them to the group tracked source
            // step2: import the UTXOS
            // step3: sync the backup data
            var identifiers = config.Derivations.Select(pair => pair.Value.Identifier!).ToArray();
            var response = await HubProxy.Handshake(new AppHandshake { Identifiers = identifiers });

            var missing = config.Derivations
                .Where(pair => response.IdentifiersAcknowledged?.Contains(pair.Value.Identifier) is not true)
                .ToList();

            foreach (var x in missing)
            {
                var result = await HubProxy.Pair(new PairRequest
                {
                    Derivations = new Dictionary<string, DerivationItem>
                    {
                        [x.Key] = new()
                        {
                            Descriptor = x.Value.Descriptor,
                            Index = x.Value.LastKnownIndex ?? 0
                        }
                    }
                });

                config.Derivations[x.Key] = new WalletDerivation
                {
                    Name = x.Value.Name,
                    Descriptor = x.Value.Descriptor,
                    Identifier = result[x.Key]
                };

                if (x.Key == WalletDerivation.LightningScripts)
                {
                    var scripts = await LDKFilter.GetWatchedOutputs(_configProvider);
                    var identifier = config.Derivations[x.Key].Identifier;
                    if (!string.IsNullOrEmpty(identifier))
                    {
                        await HubProxy.TrackScripts(identifier, scripts.Select(output => output.Script.ToHex()).ToArray());
                    }
                }
            }

            await _configProvider.Set(WalletConfig.Key, config, true);
        }
        finally
        {
            ControlSemaphore.Release();
        }
    }

    public async Task Generate()
    {
        await ControlSemaphore.WaitAsync();
        try
        {
            if (State != OnChainWalletState.NotConfigured || ReportedNetwork == null || HubProxy == null || !IsHubConnected ||
                IsConfigured(await GetConfig()) || await GetBestBlock() is not { } block)
                throw new InvalidOperationException("Cannot generate wallet in current state");

            _logger.LogInformation("Generating wallet");

            var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
            var mainnet = ReportedNetwork == Network.Main;
            var path = new KeyPath($"m/84'/{(mainnet ? "0" : "1")}'/0'");
            var fingerprint = mnemonic.DeriveExtKey().GetPublicKey().GetHDFingerPrint();
            var xpub = mnemonic.DeriveExtKey().Derive(path).Neuter().ToString(ReportedNetwork);
            var snapshot = new BlockSnapshot
            {
                BlockHash = uint256.Parse(block.BlockHash),
                BlockHeight = (uint)block.BlockHeight
            };
            var walletConfig = new WalletConfig
            {
                Birthday = snapshot,
                Mnemonic = mnemonic.ToString(),
                Network = ReportedNetwork.ToString(),
                Derivations = new Dictionary<string, WalletDerivation>
                {
                    [WalletDerivation.NativeSegwit] = new()
                    {
                        Identifier = null,
                        Name = "Native Segwit",
                        Descriptor = OutputDescriptor.AddChecksum(
                            $"wpkh([{fingerprint.ToString()}/{path}]{xpub}/0/*)")
                    }
                },
                CoinSnapshot = new CoinSnapshot
                {
                    Coins = new Dictionary<string, SavedCoin[]>(),
                    BlockSnapshot = snapshot
                }
            };

            var result = await HubProxy.Pair(new PairRequest
            {
                Derivations = walletConfig.Derivations.ToDictionary(pair => pair.Key, pair => new DerivationItem
                {
                    Descriptor = pair.Value.Descriptor
                })
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

            await _configProvider.Set(WalletConfig.Key, walletConfig, true);
            State = OnChainWalletState.Loaded;
        }
        finally
        {
            ControlSemaphore.Release();
        }
    }

    public async Task AddDerivation(string key, string name, string? descriptor)
    {
        await ControlSemaphore.WaitAsync();
        try
        {
            var config = await GetConfig();
            if (State != OnChainWalletState.Loaded || HubProxy == null || !IsHubConnected || config is null || !IsConfigured(config))
                throw new InvalidOperationException("Cannot add deriv in current state");

            if (config.Derivations.ContainsKey(key))
                throw new InvalidOperationException("Derivation already exists");

            var result = await HubProxy.Pair(new PairRequest
            {
                Derivations = new Dictionary<string, DerivationItem>
                {
                    [key] = new() { Descriptor = descriptor }
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
            ControlSemaphore.Release();
        }
    }

    private async Task ConnectionChanged(object? sender, (BTCPayConnectionState Old, BTCPayConnectionState New) valueTuple)
    {
        await DetermineState();
    }

    private async Task DetermineState()
    {
        var config = await GetConfig();
        var configured = IsConfigured(config);
        switch (IsHubConnected)
        {
            case true when configured && config!.NBitcoinNetwork != ReportedNetwork:
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
        if (HubProxy == null || !IsHubConnected)
            return [];

        var config = await GetConfig();
        if (config is null || !IsConfigured(config))
            return [];

        var identifiers = config.Derivations
            .Select(pair => pair.Value.Identifier)
            .Where(i => i != null)
            .OfType<string>()
            .ToArray();
        var response = await HubProxy.Handshake(new AppHandshake { Identifiers = identifiers });
        var missing = config.Derivations
            .Where(pair => response.IdentifiersAcknowledged?.Contains(pair.Value.Identifier) is not true)
            .ToList();

        if (missing.Count != 0)
        {
            _logger.LogWarning(
                "Some identifiers that we had asked to track were not confirmed as being listened to. Tracking will be incomplete and functionality will critically fail");
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
        var config = await GetConfig();
        var bb = await GetBestBlock();
        if (HubProxy == null || !IsHubConnected || bb is null || config is null || !IsConfigured(config))
            throw new InvalidOperationException("Cannot update snapshot in current state");

        await ControlSemaphore.WaitAsync();
        try
        {
            var identifiers = config.Derivations.Values
                .Where(d => !string.IsNullOrEmpty(d.Identifier))
                .Select(d => d.Identifier!)
                .ToArray();
            var utxos = await HubProxy.GetUTXOs(identifiers);
            config.CoinSnapshot = new CoinSnapshot
            {
                BlockSnapshot = new BlockSnapshot
                {
                    BlockHash = uint256.Parse(bb.BlockHash),
                    BlockHeight = (uint)bb.BlockHeight
                },
                Coins = utxos.ToDictionary(kv => kv.Key, kv => kv.Value.Select(coin => new SavedCoin
                {
                    Outpoint = OutPoint.Parse(coin.Outpoint!),
                    Path = coin.Path is null ? null : KeyPath.Parse(coin.Path)
                }).ToArray())
            };
            await _configProvider.Set(WalletConfig.Key, config, true);
            OnSnapshotUpdate?.Invoke(this, config.CoinSnapshot);
        }
        finally
        {
            ControlSemaphore.Release();
        }
    }

    public async Task<BitcoinAddress?> DeriveScript(string derivationId)
    {
        await ControlSemaphore.WaitAsync();
        try
        {
            var config = await GetConfig();
            if (State != OnChainWalletState.Loaded || ReportedNetwork == null || HubProxy == null || !IsHubConnected ||
                config == null || !IsConfigured(config))
                throw new InvalidOperationException("Cannot derive script in current state");

            var derivation = config.Derivations[derivationId];
            var identifier = derivation?.Identifier;
            if (derivation == null || identifier == null)
                throw new InvalidOperationException("Missing identifier");

            var addr = await HubProxy.DeriveScript(identifier);
            var keyPath = KeyPath.Parse(addr.KeyPath);
            derivation.LastKnownIndex = (int?)keyPath.Indexes.Last();
            await _configProvider.Set(WalletConfig.Key, config, true);
            return Script.FromHex(addr.Script).GetDestinationAddress(ReportedNetwork);
        }
        finally
        {
            ControlSemaphore.Release();
        }
    }

    public async Task<PSBT?> SignTransaction(byte[] psbtBytes)
    {
        if (ReportedNetwork == null)
            throw new InvalidOperationException("Cannot sign transaction in current state: No network defined");
        var psbt = PSBT.Load(psbtBytes, ReportedNetwork);
        psbt = await SignTransaction(psbt);
        return psbt;
    }

    private async Task<PSBT?> SignTransaction(PSBT psbt)
    {
        var config = await GetConfig();
        var network = config?.NBitcoinNetwork;
        if (State != OnChainWalletState.Loaded || HubProxy == null || !IsHubConnected || config == null || !IsConfigured(config) || network == null)
            throw new InvalidOperationException("Cannot sign transaction in current state");

        var identifiers = config.Derivations
            .Where(d => !string.IsNullOrEmpty(d.Value.Identifier))
            .Select(d => d.Value.Identifier!)
            .ToArray();
        var updated = await HubProxy.UpdatePsbt(identifiers, psbt.ToHex());
        psbt = PSBT.Parse(updated, network);
        var rootKey = new Mnemonic(config.Mnemonic).DeriveExtKey();
        foreach (var deriv in config.Derivations.Values.Where(derivation => derivation.Descriptor is not null))
        {
            var data = deriv.Descriptor?.ExtractFromDescriptor(network);
            if (data?.Item2 is null) continue;
            var accKey = rootKey.Derive(data.Value.Item2);
            psbt = psbt.SignAll(data.Value.Item1.AsHDScriptPubKey(data.Value.Item3), accKey);
            if (psbt.TryFinalize(out _))
                break;
        }

        return psbt;
    }

    private static ICoin ToCoin(CoinResponse response)
    {
        var outpoint = OutPoint.Parse(response.Outpoint!);
        var scriptPubKey = Script.FromHex(response.Script!);
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
    //             case SpendableOutputDescriptor.Spendatput spendableOutputDescriptorDelayedPaymentOutput:
                                                            //                 spendableOutputDescriptorDelayedPaymentOutput.delayed_payment_output.bleOutputDescriptor_DelayedPaymentOu
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

    public class CoinWithKey(OutPoint fromOutpoint, TxOut fromTxOut, Key key) : Coin(fromOutpoint, fromTxOut), ISignableCoin
    {
        public Key Key { get; } = key;

        public Task<PSBT> Sign(PSBT psbt)
        {
            return Task.FromResult(psbt.SignWithKeys(Key));
        }
    }

    public interface ISignableCoin : ICoin
    {
        Task<PSBT> Sign(PSBT psbt);
    }

    public async Task<Dictionary<string, TxResp[]>?> GetTransactions()
    {
        var config = await GetConfig();
        if (State != OnChainWalletState.Loaded || HubProxy == null || !IsHubConnected || config is null || !IsConfigured(config))
            throw new InvalidOperationException("Cannot get transactions in current state");

        var identifiersWhichWeCanDeriveKeysFor = config.Derivations.Values
            .Where(d => !string.IsNullOrEmpty(d.Identifier))
            .Select(d => d.Identifier!)
            .ToArray();
        var res = await HubProxy.GetTransactions(identifiersWhichWeCanDeriveKeysFor);
        return res.ToDictionary(
            pair =>
            {
                return config.Derivations.First(derivation =>
                    derivation.Value.Identifier?.Equals(pair.Key, StringComparison.InvariantCultureIgnoreCase) is true).Key;
            }, pair => pair.Value.OrderByDescending(tx => tx.Timestamp).ToArray());
    }

    public async Task<IEnumerable<ICoin>> GetUTXOS()
    {
        var config = await GetConfig();
        var network = config?.NBitcoinNetwork;
        if (State != OnChainWalletState.Loaded || HubProxy == null || !IsHubConnected || config is null || !IsConfigured(config) || network == null)
            throw new InvalidOperationException("Cannot get UTXOS in current state");

        var identifiers = config.Derivations.Values
            .Where(d => !string.IsNullOrEmpty(d.Identifier))
            .Select(d => d.Identifier!)
            .ToArray();
        var identifiersWhichWeCanDeriveKeysFor = config.Derivations.Values
            .Where(d => !string.IsNullOrEmpty(d.Descriptor) && !string.IsNullOrEmpty(d.Identifier))
            .Select(d => d.Identifier!.ToLowerInvariant())
            .ToArray();
        var utxos = await HubProxy.GetUTXOs(identifiers);
        var utxosThatWeCanDeriveKeysFor = utxos
            .Where(utxo => identifiersWhichWeCanDeriveKeysFor.Contains(utxo.Key.ToLowerInvariant()))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var result = new List<ICoin>();
        foreach (var kp in utxosThatWeCanDeriveKeysFor)
        {
            var derivation =
                config.Derivations.Values.First(derivation =>
                    derivation.Identifier!.Equals(kp.Key, StringComparison.InvariantCultureIgnoreCase));
            var data = derivation.Descriptor!.ExtractFromDescriptor(network);
            if (data?.Item2 is null)
                continue;

            foreach (var coin in kp.Value)
            {
                var coinKeyPath = KeyPath.Parse(coin.Path!);
                var key = new Mnemonic(config.Mnemonic).DeriveExtKey().Derive(data.Value.Item2.KeyPath).Derive(coinKeyPath).PrivateKey;
                var c = ToCoin(coin);
                result.Add(new CoinWithKey(c.Outpoint, c.TxOut, key));
            }
        }

        foreach (var kv in utxos.ExceptBy(utxosThatWeCanDeriveKeysFor.Select(pair => pair.Key), kv => kv.Key).ToArray())
        {
            foreach (var coin in kv.Value)
                result.Add(ToCoin(coin));
        }

        return result;
    }


    public async Task<(Transaction Tx, ICoin[] SpentCoins, BitcoinAddress Change)> CreateTransaction(
        List<TxOut> txOuts, FeeRate? feeRate, List<Coin>? explicitIns = null)
    {
        var config = await GetConfig();
        if (config?.NBitcoinNetwork is null)
            throw new InvalidOperationException("Cannot create transaction in current state");

        var availableCoins = (await GetUTXOS()).OfType<CoinWithKey>().ToList();
        feeRate ??= await GetFeeRate(1);

        //TODO: do not hardcode this constant
        var changeScript = await DeriveScript(WalletDerivation.NativeSegwit);
        if (changeScript is null)
            throw new InvalidOperationException("Cannot create transaction in current state");

        var txBuilder = config.NBitcoinNetwork
            .CreateTransactionBuilder()
            .SetChange(changeScript)
            .SendEstimatedFees(feeRate);

        txBuilder = txOuts.Aggregate(txBuilder, (current, c) => current.Send(c.ScriptPubKey, c.Value));
        txBuilder.SendAllRemainingToChange();

        if (explicitIns?.Any() is true)
            txBuilder.AddCoins(explicitIns.ToArray());

        while (true)
        {
            try
            {
                var tx = txBuilder.BuildTransaction(true);
                return (tx, txBuilder.FindSpentCoins(tx), changeScript);
            }
            catch (NotEnoughFundsException)
            {
                if (!availableCoins.Any()) throw;
                var newCoin = availableCoins.First();
                //TODO: switch to building a PSBT and signing with the ISignableCoin interface
                if (newCoin is { } newCoinWithKey)
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
        await ControlSemaphore.WaitAsync();
        try
        {
            var config = await GetConfig();
            if (State != OnChainWalletState.Loaded || config is null)
            {
                throw new InvalidOperationException("Cannot remove derivation in current state");
            }

            var updated = key.Aggregate(false, (current, k) => current || config.Derivations.Remove(k));
            if (updated)
                await _configProvider.Set(WalletConfig.Key, config, true);
        }
        finally
        {
            ControlSemaphore.Release();
        }
    }

    public async Task<BestBlockResponse?> GetBestBlock()
    {
        if (HubProxy == null || !IsHubConnected)
        {
            _logger.LogWarning("Cannot get best block: Hub not connected");
            return null;
        }

        var res = await _memoryCache.GetOrCreateAsync("bestblock", async entry =>
        {
            _logger.LogInformation("Getting best block");
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            try
            {
                return await HubProxy.GetBestBlock();
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
        if (HubProxy == null || !IsHubConnected)
            throw new InvalidOperationException("Cannot broadcast transaction: Hub not connected");
        await HubProxy.BroadcastTransaction(valueTx.ToHex());
    }

    public async Task<FeeRate> GetFeeRate(int blockTarget, string? subject = null)
    {
        var detail = $"fee rate for {blockTarget} blocks" + (string.IsNullOrEmpty(subject) ? "" : $" ({subject})");
        var defaultRate = new FeeRate(100m);
        if (HubProxy == null || !IsHubConnected)
        {
            _logger.LogWarning("Cannot get {Detail}: Hub not connected, using hardcoded {DefaultRate}", detail, defaultRate);
            return defaultRate;
        }

        try
        {
            return await _memoryCache.GetOrCreateAsync($"feerate_{blockTarget}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

                var feeRate = await HubProxy.GetFeeRate(blockTarget);
                var result = new FeeRate(feeRate);

                _logger.LogInformation("New {Detail}: {Result}", detail, result);
                return result;
            }) ?? defaultRate;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error getting {Detail}, using hardcoded {DefaultRate}", detail, defaultRate);
            return defaultRate;
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OnChainWalletState
{
    Init,
    NotConfigured,
    WaitingForConnection,
    Loading,
    Loaded,
    Error
}
