﻿using BTCPayApp.CommonServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Scripting;
using nldksample.LDK;

namespace BTCPayApp.Core.Attempt2;
public class OnChainWalletManager : BaseHostedService
{
    private readonly IConfigProvider _configProvider;
    private readonly BTCPayAppServerClient _btcPayAppServerClient;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly ILogger<OnChainWalletManager> _logger;
    private OnChainWalletState _state = OnChainWalletState.Init;

    public WalletConfig? WalletConfig { get; private set; }

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
        ILogger<OnChainWalletManager> logger)
    {
        _configProvider = configProvider;
        _btcPayAppServerClient = btcPayAppServerClient;
        _btcPayConnectionManager = btcPayConnectionManager;
        _logger = logger;
    }
    
    protected override async Task ExecuteStartAsync(CancellationToken cancellationToken)
    {
        State = OnChainWalletState.Loading;
        StateChanged += OnStateChanged;
        WalletConfig = await _configProvider.Get<WalletConfig>(WalletConfig.Key);
        if (WalletConfig is null)
        {
            State = OnChainWalletState.NotConfigured;
        }
        else
        {
            await Track();
        }

        _btcPayAppServerClient.OnNewBlock += OnNewBlock;
        _btcPayAppServerClient.OnTransactionDetected += OnTransactionDetected;
        _btcPayConnectionManager.ConnectionChanged += ConnectionChanged;

        State = OnChainWalletState.Loaded;
    }

    private async Task OnStateChanged(object? sender, (OnChainWalletState Old, OnChainWalletState New) e)
    {
        if (e is {New: OnChainWalletState.Loaded, Old: OnChainWalletState.NotConfigured})
        {
            await Track();
        }
    }


    public async Task Generate()
    {
        await _controlSemaphore.WaitAsync();
        try
        {
            if (State != OnChainWalletState.NotConfigured || WalletConfig is not null ||
                _btcPayConnectionManager.Connection?.State != HubConnectionState.Connected)
            {
                throw new InvalidOperationException("Cannot generate wallet in current state");
            }
            
            
            var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
            var mainnet = _btcPayConnectionManager.ReportedNetwork == Network.Main;
            var path = new KeyPath($"m/84'/{(mainnet ? "0" : "1")}'/0'");
            var fingerprint = mnemonic.DeriveExtKey().GetPublicKey().GetHDFingerPrint();
            var xpub = mnemonic.DeriveExtKey().Derive(path).Neuter().ToString(_btcPayConnectionManager.ReportedNetwork);
            
            var walletConfig = new WalletConfig()
            {
                Mnemonic = mnemonic.ToString(),
                Network = _btcPayConnectionManager.ReportedNetwork.ToString(),
                Derivations = new Dictionary<string, WalletDerivation>()
                {
                    [WalletDerivation.NativeSegwit] = new WalletDerivation()
                    {
                        Name = "Native Segwit",
                        Descriptor = OutputDescriptor.AddChecksum(
                            $"wpkh([{path.ToString().Replace("m", fingerprint.ToString())}]{xpub}/0/*)")
                    }
                }
            };
            
            var result = await _btcPayConnectionManager.HubProxy.Pair(new PairRequest()
            {   
                Derivations = WalletConfig.Derivations.ToDictionary(pair => pair.Key, pair => pair.Value.Descriptor)
            });
            foreach (var keyValuePair in result)
            {
                WalletConfig.Derivations[keyValuePair.Key].Identifier = keyValuePair.Value;
                
            }
            await _configProvider.Set(WalletConfig.Key, walletConfig);
            WalletConfig = walletConfig;
            State= OnChainWalletState.Loaded;
            
            
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
            if (State != OnChainWalletState.Loaded || WalletConfig is null ||
                _btcPayConnectionManager.Connection?.State != HubConnectionState.Connected)
            {
                throw new InvalidOperationException("Cannot add deriv in current state");
            }
            if(WalletConfig.Derivations.ContainsKey(key))
                throw new InvalidOperationException("Derivation already exists");
            
            var result = await _btcPayConnectionManager.HubProxy.Pair(new PairRequest()
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
        if (hubConnectionState == HubConnectionState.Connected && State == OnChainWalletState.Loaded)
        {
            await Track();
        }
    }

    private async Task Track()
    {
        if (WalletConfig is null)
            return;

        var identifiers = WalletConfig.Derivations.Select(pair => pair.Value.Identifier).ToArray();
        var response = await _btcPayConnectionManager.HubProxy.Handshake(new AppHandshake()
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

    private Task OnTransactionDetected(object? sender, string e)
    {
        throw new NotImplementedException();
    }

    private Task OnNewBlock(object? sender, string e)
    {
        throw new NotImplementedException();
    }

    public async Task<Script> DeriveScript(string derivation)
    {
        var identifier = WalletConfig?.Derivations[derivation].Identifier;
        var addr = await _btcPayConnectionManager.HubProxy.DeriveScript(identifier);
        return Script.FromHex(addr);
    }

}

public enum OnChainWalletState
{
    Init,
    NotConfigured,
    Loading,
    Loaded
}