using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Contracts;
using BTCPayApp.UI.Features;
using Fluxor;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;

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
                await configProvider.Set(UiStateConfigKey, state, false);
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

        lightningNodeService.StateChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(lightningNodeService.State));
            return Task.CompletedTask;
        };

        onChainWalletManager.StateChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(onChainWalletManager.State));
            return Task.CompletedTask;
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
}
