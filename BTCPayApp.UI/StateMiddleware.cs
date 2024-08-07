using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.UI.Features;
using Fluxor;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayApp.UI;

public class StateMiddleware(
    IConfigProvider configProvider,
    BTCPayConnectionManager btcPayConnectionManager,
    LightningNodeManager lightningNodeService,
    OnChainWalletManager onChainWalletManager,
    BTCPayAppServerClient btcpayAppServerClient,
    IAccountManager accountManager,
    NavigationManager navigationManager,
    ILogger<StateMiddleware> logger,
    IDispatcher _dispatcher)
    : Middleware
{
    public const string UiStateConfigKey = "uistate";
    private CancellationTokenSource? _ratesCts;

    public override async Task InitializeAsync(IDispatcher dispatcher, IStore store)
    {
        if (store.Features.TryGetValue(typeof(UIState).FullName, out var uiStateFeature))
        {
            var existing = await configProvider.Get<UIState>(UiStateConfigKey);
            if (existing is not null)
            {
                uiStateFeature.RestoreState(existing);
            }
            uiStateFeature.StateChanged += async (sender, args) =>
            {
                var state = (UIState)uiStateFeature.GetState() with { Instance = null };
                await configProvider.Set(UiStateConfigKey, state, false);
            };

            store.Initialized.ContinueWith(task => ListenIn(dispatcher));
        }

        await base.InitializeAsync(dispatcher, store);
    }

    private async Task RefreshRates(IDispatcher dispatcher, CancellationToken token)
    {
        while (token.IsCancellationRequested is false)
        {
            var storeInfo = accountManager.GetCurrentStore();
            if (storeInfo != null) dispatcher.Dispatch(new StoreState.FetchRates(storeInfo));
            await Task.Delay(TimeSpan.FromMinutes(5), token);
        }
    }

    private void ListenIn(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(btcPayConnectionManager.ConnectionState));
        dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(lightningNodeService.State));
        dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(onChainWalletManager.State));

        btcPayConnectionManager.ConnectionChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(btcPayConnectionManager.ConnectionState));
            return Task.CompletedTask;
        };

        onChainWalletManager.StateChanged += async (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(onChainWalletManager.State));
            if (onChainWalletManager.State == OnChainWalletState.Loaded)
                await TryApplyingAppPaymentMethodsToCurrentStore(true, false);
        };

        lightningNodeService.StateChanged += async (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(lightningNodeService.State));
            if (lightningNodeService.State == LightningNodeState.Loaded)
            {
                await TryApplyingAppPaymentMethodsToCurrentStore(false, true);
            }
        };

        accountManager.OnAfterStoreChange += async (sender, storeInfo) =>
        {
            dispatcher.Dispatch(new StoreState.SetStoreInfo(storeInfo));
            if (storeInfo != null)
            {
                navigationManager.NavigateTo(Routes.Dashboard);
                await TryApplyingAppPaymentMethodsToCurrentStore(true, true);
            }
            else
            {
                navigationManager.NavigateTo(Routes.SelectStore, true, true);
            }
        };

        btcpayAppServerClient.OnNotifyServerEvent += async (sender, serverEvent) =>
        {
            logger.LogDebug("Received Server Event: {Type} - {Details}", serverEvent.Type, serverEvent.ToString());
            var currentUserId = accountManager.GetUserInfo()?.UserId;
            if (string.IsNullOrEmpty(currentUserId)) return;
            var currentStoreId = accountManager.GetCurrentStore()?.Id;
            switch (serverEvent.Type)
            {
                case "user-updated":
                    if (currentUserId == serverEvent.UserId)
                        await accountManager.CheckAuthenticated(true);
                    break;
                case "user-deleted":
                    if (currentUserId == serverEvent.UserId)
                        await accountManager.Logout();
                    break;
                case "notifications-updated":
                    if (currentStoreId != null)
                        dispatcher.Dispatch(new StoreState.FetchNotifications(currentStoreId));
                    break;
                case "invoice-updated":
                    if (serverEvent.StoreId != null && serverEvent.StoreId == currentStoreId)
                        dispatcher.Dispatch(new StoreState.FetchInvoices(serverEvent.StoreId));
                    break;
                case "store-created":
                case "store-updated":
                case "store-removed":
                case "user-store-added":
                case "user-store-updated":
                case "user-store-removed":
                    if (serverEvent.StoreId != null)
                    {
                        await accountManager.CheckAuthenticated(true);
                        if (serverEvent.Type is "store-removed" or "user-store-removed" && serverEvent.StoreId == currentStoreId)
                        {
                            await accountManager.UnsetCurrentStore();
                        }
                    }
                    break;
            }
        };

        _ratesCts = new CancellationTokenSource();
        _ = RefreshRates(dispatcher, _ratesCts.Token);
    }

    private async Task<(GenericPaymentMethodData? onchain, GenericPaymentMethodData? lightning)?> TryApplyingAppPaymentMethodsToCurrentStore(bool applyOnchain, bool applyLighting)
    {
        var storeId = accountManager.GetCurrentStore()?.Id;
        if (// is a store present?
            string.IsNullOrEmpty(storeId) ||
            // is user permitted? (store owner)
            !await accountManager.IsAuthorized(Policies.CanModifyStoreSettings, storeId) ||
            // is the onchain wallet configured?
            !onChainWalletManager.IsConfigured) return null;
        // check the store's payment methods
        var pms = await accountManager.GetClient().GetStorePaymentMethods(storeId, includeConfig: true);
        // onchain
        var onchain = pms.FirstOrDefault(pm => pm.PaymentMethodId == OnChainWalletManager.PaymentMethodId);
        if (applyOnchain)
        {
            var onchainDerivation = onChainWalletManager.Derivation;
            if (onchain is null && onchainDerivation is not null)
                onchain = await accountManager.GetClient().UpdateStorePaymentMethod(storeId, OnChainWalletManager.PaymentMethodId, new UpdatePaymentMethodRequest
                {
                    Enabled = true,
                    Config = onchainDerivation.Descriptor
                });
            if (onchain?.Config is JObject configObj &&
                configObj.TryGetValue("accountDerivation", out var derivationSchemeToken) &&
                derivationSchemeToken.Value<string>() is { } derivationScheme &&
                onchainDerivation?.Identifier.Contains(derivationScheme) is true)
            {
                _dispatcher.Dispatch(new StoreState.FetchOnchainBalance(storeId));
                _dispatcher.Dispatch(new StoreState.FetchOnchainHistogram(storeId));
            }
        }
        // lightning
        var lightning = pms.FirstOrDefault(pm => pm.PaymentMethodId == LightningNodeManager.PaymentMethodId);
        if (applyLighting)
        {
            if (lightning is null && !string.IsNullOrEmpty(lightningNodeService.ConnectionString))
                lightning = await accountManager.GetClient().UpdateStorePaymentMethod(storeId, LightningNodeManager.PaymentMethodId, new UpdatePaymentMethodRequest
                {
                    Enabled = true,
                    Config = new JObject { ["connectionString"] = lightningNodeService.ConnectionString }
                });
            if (lightning?.Config is JObject configObj &&
                configObj.TryGetValue("connectionString", out var configuredConnectionStringToken) &&
                configuredConnectionStringToken.Value<string>() is { } configuredConnectionString &&
                configuredConnectionString == lightningNodeService.ConnectionString)
            {
                _dispatcher.Dispatch(new StoreState.FetchLightningBalance(storeId));
                _dispatcher.Dispatch(new StoreState.FetchLightningHistogram(storeId));
            }
        }
        return (onchain, lightning);
    }
}
