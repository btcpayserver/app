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
    private bool _previouslyConnected;

    public override async Task InitializeAsync(IDispatcher dispatcher, IStore store)
    {
        if (store.Features.TryGetValue(typeof(UIState).FullName, out var uiStateFeature))
        {
            var existing = await configProvider.Get<UIState>(UiStateConfigKey);
            if (existing is not null)
            {
                uiStateFeature.RestoreState(existing);
            }
            uiStateFeature.StateChanged += async (_, _) =>
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
            var storeInfo = accountManager.CurrentStore;
            if (storeInfo != null) dispatcher.Dispatch(new StoreState.FetchRates(storeInfo));
            await Task.Delay(TimeSpan.FromMinutes(5), token);
        }
    }

    private async Task ListenIn(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(btcPayConnectionManager.ConnectionState));
        dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(lightningNodeService.State));
        dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(onChainWalletManager.State));
        dispatcher.Dispatch(new UserState.SetInfo(accountManager.UserInfo, null));

        btcPayConnectionManager.ConnectionChanged += async (_, _) =>
        {
            dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(btcPayConnectionManager.ConnectionState));

            // initial wallet generation
            if (onChainWalletManager is { State: OnChainWalletState.NotConfigured } && await onChainWalletManager.CanConfigureWallet())
            {
                await onChainWalletManager.Generate();
            }

            // refresh after returning from the background
            if (btcPayConnectionManager.ConnectionState == BTCPayConnectionState.ConnectedFinishedInitialSync && !_previouslyConnected)
            {
                _previouslyConnected = true;
            }
            else if (btcPayConnectionManager.ConnectionState == BTCPayConnectionState.Syncing && _previouslyConnected && accountManager.CurrentStore is { } store)
            {
                dispatcher.Dispatch(new StoreState.RefreshStore(store));
            }
        };

        onChainWalletManager.StateChanged += async (_, _) =>
        {
            dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(onChainWalletManager.State));
            if (accountManager.CurrentStore is { } store)
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

        onChainWalletManager.OnSnapshotUpdate += (_, _) =>
        {
            if (accountManager.CurrentStore is { } store)
            {
                dispatcher.Dispatch(new StoreState.FetchBalances(store.Id));
            }
            return Task.CompletedTask;
        };

        lightningNodeService.StateChanged += async (_, _) =>
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
                    if (accountManager.CurrentStore is { } store)
                    {
                        dispatcher.Dispatch(new StoreState.FetchLightningBalance(store.Id));
                        dispatcher.Dispatch(new StoreState.FetchLightningHistogram(store.Id));
                    }
                }
            }
        };

        accountManager.OnStoreChanged += async (_, storeInfo) =>
        {
            dispatcher.Dispatch(new StoreState.SetStoreInfo(storeInfo));
            if (storeInfo != null)
            {
                var res = await accountManager.TryApplyingAppPaymentMethodsToCurrentStore(onChainWalletManager, lightningNodeService, true, true);
                var refresh = res is { onchain: {} onchain } && await onChainWalletManager.IsOnChainOurs(onchain) ||
                                   res is { lightning: {} lightning } && await lightningNodeService.IsLightningOurs(lightning);
                if (refresh)
                    dispatcher.Dispatch(new StoreState.FetchBalances(storeInfo.Id));
                if (storeInfo.PosAppId != null)
                    dispatcher.Dispatch(new StoreState.FetchPointOfSaleStats(storeInfo.PosAppId));
            }

            navigationManager.NavigateTo(Routes.Index);
        };

        accountManager.OnUserInfoChanged += (_, userInfo) =>
        {
            dispatcher.Dispatch(new UserState.SetInfo(userInfo, null));
            return Task.CompletedTask;
        };

        btcpayAppServerClient.OnNotifyServerEvent += async (_, serverEvent) =>
        {
            logger.LogDebug("Received Server Event: {Type} - {Info} ({Detail})", serverEvent.Type, serverEvent.ToString(), serverEvent.Detail ?? "no details");
            var currentUserId = accountManager.UserInfo?.UserId;
            if (string.IsNullOrEmpty(currentUserId)) return;
            var currentStore = accountManager.CurrentStore;
            var isCurrentUser = serverEvent.UserId == currentUserId;
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
                case "store-user-added":
                case "store-user-updated":
                case "store-user-removed":
                    if (serverEvent.StoreId != null)
                    {
                        await accountManager.CheckAuthenticated(true);
                        if (currentStore == null || !isCurrentStore) return;
                        if (serverEvent.Type is "store-user-removed" && isCurrentUser)
                            await accountManager.SetCurrentStoreId(null);
                        if (serverEvent.Type is "store-removed")
                            await accountManager.SetCurrentStoreId(null);
                        if (serverEvent.Type is "store-updated")
                            dispatcher.Dispatch(new StoreState.FetchStore(serverEvent.StoreId!));
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
