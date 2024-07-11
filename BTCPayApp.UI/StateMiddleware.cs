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
    ILogger<StateMiddleware> logger)
    : Middleware
{
    public const string UiStateConfigKey = "uistate";

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
                await configProvider.Set(UiStateConfigKey, state);
            };

            store.Initialized.ContinueWith(task => ListenIn(dispatcher));
        }

        await base.InitializeAsync(dispatcher, store);
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
                await TryApplyingAppPaymentMethodsToCurrentStore(false, true);
        };

        accountManager.OnAfterStoreChange += async (sender, storeInfo) =>
        {
            await TryApplyingAppPaymentMethodsToCurrentStore(true, true);
        };

        btcpayAppServerClient.OnNotifyServerEvent += async (sender, serverEvent) =>
        {
            logger.LogDebug("Received Server Event: {Type} - {Details}", serverEvent.Type, serverEvent.ToString());
            var currentUserId = accountManager.GetUserInfo()?.UserId;
            if (string.IsNullOrEmpty(currentUserId)) return;
            var currentStoreId = accountManager.GetCurrentStore()?.Id;
            string? eventStoreId = null;
            switch (serverEvent.Type)
            {
                case "notifications-updated":
                    if (currentStoreId != null)
                        dispatcher.Dispatch(new StoreState.FetchNotifications(currentStoreId));
                    else
                        dispatcher.Dispatch(new NotificationState.FetchNotifications());
                    break;
                case "invoice-updated":
                    if (serverEvent.StoreId != null && serverEvent.StoreId == currentStoreId) dispatcher.Dispatch(new StoreState.FetchInvoices(serverEvent.StoreId));
                    break;
                case "store-created":
                case "store-updated":
                case "store-removed":
                case "user-store-added":
                case "user-store-updated":
                case "user-store-removed":
                    if (serverEvent.StoreId != null)
                    {
                        // TODO: Move manager actions to state and dispatch them
                        await accountManager.CheckAuthenticated(true);
                        if (serverEvent.Type is "store-removed" or "user-store-removed" &&
                            serverEvent.StoreId == currentStoreId)
                        {
                            await accountManager.UnsetCurrentStore();
                            navigationManager.NavigateTo(Routes.SelectStore, true, true);
                        }
                    }
                    break;
            }
        };
    }

    private async Task<(GenericPaymentMethodData? onchain, GenericPaymentMethodData? lightning)?> TryApplyingAppPaymentMethodsToCurrentStore(bool applyOnchain, bool applyLighting)
    {
        var storeId = accountManager.GetCurrentStore()?.Id;
        if (// is a store present?
            string.IsNullOrEmpty(storeId) ||
            // is user permitted? (store owner)
            !await accountManager.IsAuthorized(Policies.CanModifyStoreSettings, storeId) ||
            // is the onchain wallet configured?
            onChainWalletManager.WalletConfig?.Derivations.TryGetValue(WalletDerivation.NativeSegwit, out var onchainDerivation) is not true || string.IsNullOrEmpty(onchainDerivation.Descriptor)) return null;
        // check the store's payment methods
        var pms = await accountManager.GetClient().GetStorePaymentMethods(storeId);
        var onchain = pms.FirstOrDefault(pm => pm.PaymentMethodId == OnChainWalletManager.PaymentMethodId);
        if (onchain is null && applyOnchain)
        {
            onchain = await accountManager.GetClient().UpdateStorePaymentMethod(storeId, OnChainWalletManager.PaymentMethodId, new UpdatePaymentMethodRequest
            {
                Enabled = true,
                Config = onchainDerivation.Descriptor
            });
        }
        var lightning = pms.FirstOrDefault(pm => pm.PaymentMethodId == LightningNodeManager.PaymentMethodId);
        if (lightning is null && !string.IsNullOrEmpty(lightningNodeService.ConnectionString) && applyLighting)
        {
            lightning = await accountManager.GetClient().UpdateStorePaymentMethod(storeId, LightningNodeManager.PaymentMethodId, new UpdatePaymentMethodRequest
            {
                Enabled = true,
                Config = new JObject { ["connectionString"] = lightningNodeService.ConnectionString }
            });
        }
        return (onchain, lightning);
    }
}
