using System.Text.Json;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.Wallet;
using BTCPayApp.UI.Features;
using Fluxor;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;

namespace BTCPayApp.UI;

public class StateMiddleware(
    ConfigProvider configProvider,
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

            _ = store.Initialized.ContinueWith(_ => ListenIn(dispatcher));
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

    private async Task ListenIn(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(btcPayConnectionManager.ConnectionState));
        dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(lightningNodeService.State));
        dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(onChainWalletManager.State));
        dispatcher.Dispatch(new UserState.SetInfo(accountManager.GetUserInfo(), null));

        btcPayConnectionManager.ConnectionChanged += async (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(btcPayConnectionManager.ConnectionState));

            // initial wallet generation
            if (onChainWalletManager is { State: OnChainWalletState.NotConfigured } && await onChainWalletManager.CanConfigureWallet())
            {
                await onChainWalletManager.Generate();
            }
        };

        onChainWalletManager.StateChanged += async (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(onChainWalletManager.State));
            if (accountManager.GetCurrentStore() is { } store)
            {
                switch (onChainWalletManager.State)
                {
                    case OnChainWalletState.Loaded:
                        var res = await accountManager.TryApplyingAppPaymentMethodsToCurrentStore(onChainWalletManager, lightningNodeService, true, false);
                        if (res is { onchain: {} onchain } &&  await onChainWalletManager.IsOnChainOurs(onchain))
                        {
                            _dispatcher.Dispatch(new StoreState.FetchOnchainBalance(store.Id));
                            _dispatcher.Dispatch(new StoreState.FetchOnchainHistogram(store.Id));
                        }
                        break;
                    case OnChainWalletState.NotConfigured when await onChainWalletManager.CanConfigureWallet():
                        await onChainWalletManager.Generate();
                        break;
                }
            }
        };

        lightningNodeService.StateChanged += async (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(lightningNodeService.State));
            if (lightningNodeService is {State: LightningNodeState.NotConfigured} && await lightningNodeService.CanConfigureLightningNode())
            {
                try
                {
                    await lightningNodeService.Generate();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error configuring LN wallet");
                }
            }
            if (lightningNodeService.State == LightningNodeState.Loaded)
            {
                var res = await accountManager.TryApplyingAppPaymentMethodsToCurrentStore(onChainWalletManager, lightningNodeService, false, true);
                if (res is { lightning: {} lightning } && await lightningNodeService.IsLightningOurs(lightning))
                {
                    if (accountManager.GetCurrentStore() is { } store)
                    {
                        dispatcher.Dispatch(new StoreState.FetchLightningBalance(store.Id));
                        dispatcher.Dispatch(new StoreState.FetchLightningHistogram(store.Id));
                    }
                }
            }
        };

        accountManager.OnAfterStoreChange += async (sender, storeInfo) =>
        {
            dispatcher.Dispatch(new StoreState.SetStoreInfo(storeInfo));
            if (storeInfo != null)
            {
                navigationManager.NavigateTo(Routes.Dashboard);
               var res = await accountManager.TryApplyingAppPaymentMethodsToCurrentStore(onChainWalletManager, lightningNodeService, true, true);
                if (res is { onchain: {} onchain } && await onChainWalletManager.IsOnChainOurs(onchain))
                {
                    dispatcher.Dispatch(new StoreState.FetchOnchainBalance(storeInfo.Id));
                    dispatcher.Dispatch(new StoreState.FetchOnchainHistogram(storeInfo.Id));
                }
                if (res is { lightning: {} lightning } && await lightningNodeService.IsLightningOurs(lightning))
                {
                    dispatcher.Dispatch(new StoreState.FetchLightningBalance(storeInfo.Id));
                    dispatcher.Dispatch(new StoreState.FetchLightningHistogram(storeInfo.Id));
                }
                dispatcher.Dispatch(new StoreState.FetchBalances(storeInfo.Id));
                if (storeInfo.PosAppId != null)
                    dispatcher.Dispatch(new StoreState.FetchPointOfSaleStats(storeInfo.PosAppId));
            }
            else
            {
                navigationManager.NavigateTo(Routes.SelectStore, true, true);
            }
        };

        accountManager.OnUserInfoChange += (sender, userInfo) =>
        {
            dispatcher.Dispatch(new UserState.SetInfo(userInfo, null));
            return Task.CompletedTask;
        };

        btcpayAppServerClient.OnNotifyServerEvent += async (sender, serverEvent) =>
        {
            logger.LogDebug("Received Server Event: {Type} - {Info} ({Detail})", serverEvent.Type, serverEvent.ToString(), serverEvent.Detail ?? "no details");
            var currentUserId = accountManager.GetUserInfo()?.UserId;
            if (string.IsNullOrEmpty(currentUserId)) return;
            var currentStore = accountManager.GetCurrentStore();
            var isCurrentStore = serverEvent.StoreId != null && currentStore != null && serverEvent.StoreId == currentStore.Id;
            switch (serverEvent.Type)
            {
                case "app-created":
                    if (isCurrentStore && currentStore!.PosAppId == null)
                        dispatcher.Dispatch(new StoreState.FetchPointOfSale(currentStore.PosAppId!));
                    break;
                case "app-deleted":
                    if (isCurrentStore && currentStore!.PosAppId == serverEvent.AppId)
                    {
                        var store = await accountManager.EnsureStorePos(currentStore, true);
                        dispatcher.Dispatch(new StoreState.FetchPointOfSale(store.PosAppId!));
                    }
                    break;
                case "app-updated":
                    if (isCurrentStore && currentStore!.PosAppId == serverEvent.AppId)
                        dispatcher.Dispatch(new StoreState.FetchPointOfSale(currentStore.PosAppId!));
                    break;
                case "user-updated":
                    if (currentUserId == serverEvent.UserId)
                        await accountManager.CheckAuthenticated(true);
                    break;
                case "user-deleted":
                    if (currentUserId == serverEvent.UserId)
                        await accountManager.Logout();
                    break;
                case "notifications-updated":
                    if (currentStore != null)
                        dispatcher.Dispatch(new StoreState.FetchNotifications(currentStore.Id));
                    break;
                case "invoice-updated":
                    if (isCurrentStore)
                    {
                        dispatcher.Dispatch(new StoreState.FetchInvoices(serverEvent.StoreId!));
                        if (serverEvent.Detail is "Processing" or "Settled")
                        {
                            dispatcher.Dispatch(new StoreState.FetchBalances(serverEvent.StoreId!));
                            if (currentStore!.PosAppId != null)
                                dispatcher.Dispatch(new StoreState.FetchPointOfSaleStats(currentStore.PosAppId));
                        }
                    }
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
                        if (serverEvent.Type is "store-removed" or "user-store-removed" && currentStore != null && serverEvent.StoreId == currentStore.Id)
                        {
                            await accountManager.UnsetCurrentStore();
                        }
                    }
                    break;
            }
        };

        _ratesCts = new CancellationTokenSource();
        _ = RefreshRates(dispatcher, _ratesCts.Token);

        // initial wallet generation
        if (onChainWalletManager is { State: OnChainWalletState.NotConfigured } && await onChainWalletManager.CanConfigureWallet())
        {
            await onChainWalletManager.Generate();
        }
    }
}
