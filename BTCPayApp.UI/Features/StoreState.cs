using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Models;
using BTCPayApp.UI.Util;
using BTCPayServer.Client.Models;
using Fluxor;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record StoreState
{
    public AppUserStoreInfo? StoreInfo;
    public RemoteData<StoreData>? Store;
    public RemoteData<OnChainWalletOverviewData>? OnchainBalance;
    public RemoteData<HistogramData>? OnchainHistogram;
    public RemoteData<LightningNodeBalanceData>? LightningBalance;
    public RemoteData<HistogramData>? LightningHistogram;
    public RemoteData<PointOfSaleAppData>? PointOfSale;
    public RemoteData<AppSalesStats>? PosSalesStats;
    public RemoteData<List<AppItemStats>>? PosItemStats;
    public RemoteData<IEnumerable<StoreRateResult>>? Rates;
    public RemoteData<IEnumerable<InvoiceData>>? Invoices;
    public RemoteData<IEnumerable<StoreUserData>>? Users;
    public RemoteData<IEnumerable<RoleData>>? Roles;
    public RemoteData<IEnumerable<NotificationData>>? Notifications;
    private IDictionary<string,RemoteData<InvoiceData>?> _invoicesById = new Dictionary<string, RemoteData<InvoiceData>?>();
    private IDictionary<string,RemoteData<InvoicePaymentMethodDataModel[]>?> _invoicePaymentMethodsById = new Dictionary<string, RemoteData<InvoicePaymentMethodDataModel[]>?>();
    public HistogramData? UnifiedHistogram;
    public HistogramType? HistogramType;

    private static readonly string[] BitcoinUnits = [CurrencyDisplay.BTC, CurrencyDisplay.SATS];

    public record SetStoreInfo(AppUserStoreInfo? StoreInfo);
    public record SetHistogramType(HistogramType Type);
    public record FetchStore(string StoreId);
    public record FetchOnchainBalance(string StoreId);
    public record FetchLightningBalance(string StoreId);
    public record FetchOnchainHistogram(string StoreId, HistogramType? Type = null);
    public record FetchLightningHistogram(string StoreId, HistogramType? Type = null);
    public record FetchHistograms(string StoreId, HistogramType? Type = null);
    public record FetchBalances(string StoreId, HistogramType? Type = null);
    public record FetchNotifications(string StoreId);
    public record UpdateNotification(string NotificationId, bool Seen);
    public record SetNotification(NotificationData? Notification, string? Error);
    public record FetchRoles(string StoreId);
    public record FetchUsers(string StoreId);
    public record FetchInvoices(string StoreId);
    public record FetchInvoice(string StoreId, string InvoiceId);
    public record FetchInvoicePaymentMethods(string StoreId, string InvoiceId);
    public record FetchRates(AppUserStoreInfo Store);
    public record FetchPointOfSale(string AppId);
    public record FetchPointOfSaleStats(string AppId);
    public record FetchPosItemStats(string AppId);
    public record FetchPosSalesStats(string AppId);
    public record UpdateStore(string StoreId, UpdateStoreRequest Request);
    public record UpdatedStore(StoreData? Store, string? Error) : SetStore(Store, Error);
    public record UpdatePointOfSale(string AppId, PointOfSaleAppRequest Request);
    public record SetStore(StoreData? Store, string? Error);
    public record SetOnchainBalance(OnChainWalletOverviewData? Overview, string? Error);
    public record SetLightningBalance(LightningNodeBalanceData? Balance, string? Error);
    public record SetOnchainHistogram(HistogramData? Data, string? Error);
    public record SetLightningHistogram(HistogramData? Data, string? Error);
    public record SetRoles(IEnumerable<RoleData>? Roles, string? Error);
    public record SetUsers(IEnumerable<StoreUserData>? Users, string? Error);
    public record SetNotifications(IEnumerable<NotificationData>? Notifications, string? Error);
    public record SetInvoices(IEnumerable<InvoiceData>? Invoices, string? Error);
    public record SetInvoice(InvoiceData? Invoice, string? Error, string InvoiceId);
    public record SetInvoicePaymentMethods(InvoicePaymentMethodDataModel[]? PaymentMethods, string? Error, string InvoiceId);
    public record SetRates(IEnumerable<StoreRateResult>? Rates, string? Error);
    public record SetPointOfSale(PointOfSaleAppData? AppData, string? Error);
    public record UpdatedPointOfSale(PointOfSaleAppData? AppData, string? Error) : SetPointOfSale(AppData, Error);
    public record SetPosItemStats(List<AppItemStats>? ItemStats, string? Error);
    public record SetPosSalesStats(AppSalesStats? SalesStats, string? Error);

    protected class SetStoreInfoReducer : Reducer<StoreState, SetStoreInfo>
    {
        public override StoreState Reduce(StoreState state, SetStoreInfo action)
        {
            return state with
            {
                StoreInfo = action.StoreInfo,
                Store = new RemoteData<StoreData>(),
                OnchainBalance = new RemoteData<OnChainWalletOverviewData>(),
                LightningBalance = new RemoteData<LightningNodeBalanceData>(),
                OnchainHistogram = new RemoteData<HistogramData>(),
                LightningHistogram = new RemoteData<HistogramData>(),
                PointOfSale = new RemoteData<PointOfSaleAppData>(),
                PosItemStats = new RemoteData<List<AppItemStats>>(),
                PosSalesStats = new RemoteData<AppSalesStats>(),
                Rates = new RemoteData<IEnumerable<StoreRateResult>>(),
                Invoices = new RemoteData<IEnumerable<InvoiceData>>(),
                Notifications = new RemoteData<IEnumerable<NotificationData>>(),
                _invoicesById = new Dictionary<string, RemoteData<InvoiceData>?>(),
                _invoicePaymentMethodsById = new Dictionary<string, RemoteData<InvoicePaymentMethodDataModel[]>?>(),
                UnifiedHistogram = null,
                HistogramType = null
            };
        }
    }


    protected class SetHistogramTypeReducer : Reducer<StoreState, SetHistogramType>
    {
        public override StoreState Reduce(StoreState state, SetHistogramType action)
        {
            return state with
            {
                HistogramType = action.Type,
            };
        }
    }

    protected class FetchStoreReducer : Reducer<StoreState, FetchStore>
    {
        public override StoreState Reduce(StoreState state, FetchStore action)
        {
            return state with
            {
                Store = (state.Store ?? new RemoteData<StoreData>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchOnchainBalanceReducer : Reducer<StoreState, FetchOnchainBalance>
    {
        public override StoreState Reduce(StoreState state, FetchOnchainBalance action)
        {
            return state with
            {
                OnchainBalance = (state.OnchainBalance ?? new RemoteData<OnChainWalletOverviewData>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchLightningBalanceReducer : Reducer<StoreState, FetchLightningBalance>
    {
        public override StoreState Reduce(StoreState state, FetchLightningBalance action)
        {
            return state with
            {
                LightningBalance = (state.LightningBalance ?? new RemoteData<LightningNodeBalanceData>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchRolesReducer : Reducer<StoreState, FetchRoles>
    {
        public override StoreState Reduce(StoreState state, FetchRoles action)
        {
            return state with
            {
                Roles = (state.Roles ?? new RemoteData<IEnumerable<RoleData>>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchUsersReducer : Reducer<StoreState, FetchUsers>
    {
        public override StoreState Reduce(StoreState state, FetchUsers action)
        {
            return state with
            {
                Users = (state.Users ?? new RemoteData<IEnumerable<StoreUserData>>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchNotificationsReducer : Reducer<StoreState, FetchNotifications>
    {
        public override StoreState Reduce(StoreState state, FetchNotifications action)
        {
            return state with
            {
                Notifications = (state.Notifications ?? new RemoteData<IEnumerable<NotificationData>>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchInvoicesReducer : Reducer<StoreState, FetchInvoices>
    {
        public override StoreState Reduce(StoreState state, FetchInvoices action)
        {
            return state with
            {
                Invoices = (state.Invoices ?? new RemoteData<IEnumerable<InvoiceData>>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchInvoiceReducer : Reducer<StoreState, FetchInvoice>
    {
        public override StoreState Reduce(StoreState state, FetchInvoice action)
        {
            var invoice = GetInvoice(state, action.InvoiceId)?.Data;
            if (state._invoicesById.ContainsKey(action.InvoiceId))
                state._invoicesById.Remove(action.InvoiceId);
            return state with
            {
                _invoicesById = new Dictionary<string, RemoteData<InvoiceData>?>(state._invoicesById)
                {
                    { action.InvoiceId, new RemoteData<InvoiceData>(invoice, null, true) }
                }
            };
        }
    }

    protected class FetchInvoicePaymentMethodsReducer : Reducer<StoreState, FetchInvoicePaymentMethods>
    {
        public override StoreState Reduce(StoreState state, FetchInvoicePaymentMethods action)
        {
            var pms = GetInvoicePaymentMethods(state, action.InvoiceId)?.Data;
            if (state._invoicePaymentMethodsById.ContainsKey(action.InvoiceId))
                state._invoicePaymentMethodsById.Remove(action.InvoiceId);
            return state with
            {
                _invoicePaymentMethodsById = new Dictionary<string, RemoteData<InvoicePaymentMethodDataModel[]>?>(state._invoicePaymentMethodsById)
                {
                    { action.InvoiceId, new RemoteData<InvoicePaymentMethodDataModel[]>(pms, null, true) }
                }
            };
        }
    }

    protected class FetchRatesReducer : Reducer<StoreState, FetchRates>
    {
        public override StoreState Reduce(StoreState state, FetchRates action)
        {
            return state with
            {
                Rates = (state.Rates ?? new RemoteData<IEnumerable<StoreRateResult>>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchPointOfSaleReducer : Reducer<StoreState, FetchPointOfSale>
    {
        public override StoreState Reduce(StoreState state, FetchPointOfSale action)
        {
            return state with
            {
                PointOfSale = (state.PointOfSale ?? new RemoteData<PointOfSaleAppData>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchPointOfSaleStatsReducer : Reducer<StoreState, FetchPointOfSaleStats>
    {
        public override StoreState Reduce(StoreState state, FetchPointOfSaleStats action)
        {
            return state with
            {
                PosItemStats = (state.PosItemStats ?? new RemoteData<List<AppItemStats>>()) with
                {
                    Loading = true
                },
                PosSalesStats = (state.PosSalesStats ?? new RemoteData<AppSalesStats>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class SetStoreReducer : Reducer<StoreState, SetStore>
    {
        public override StoreState Reduce(StoreState state, SetStore action)
        {
            return state with
            {
                Store = (state.Store ?? new RemoteData<StoreData>()) with {
                    Data = action.Store ?? state.Store?.Data,
                    Error = action.Error,
                    Loading = false,
                    Sending = false
                }
            };
        }
    }

    protected class UpdateStoreReducer : Reducer<StoreState, UpdateStore>
    {
        public override StoreState Reduce(StoreState state, UpdateStore action)
        {
            return state with
            {
                Store = (state.Store ?? new RemoteData<StoreData>()) with
                {
                    Sending = true
                }
            };
        }
    }

    protected class SetOnchainBalanceReducer : Reducer<StoreState, SetOnchainBalance>
    {
        public override StoreState Reduce(StoreState state, SetOnchainBalance action)
        {
            return state with
            {
                OnchainBalance = (state.OnchainBalance ?? new RemoteData<OnChainWalletOverviewData>()) with {
                    Data = action.Overview ?? state.OnchainBalance?.Data,
                    Error = action.Error,
                    Loading = false,
                    Sending = false
                }
            };
        }
    }

    protected class SetLightningBalanceReducer : Reducer<StoreState, SetLightningBalance>
    {
        public override StoreState Reduce(StoreState state, SetLightningBalance action)
        {
            return state with
            {
                LightningBalance = (state.LightningBalance ?? new RemoteData<LightningNodeBalanceData>()) with {
                    Data = action.Balance ?? state.LightningBalance?.Data,
                    Error = action.Error,
                    Loading = false,
                    Sending = false
                }
            };
        }
    }

    protected class SetOnchainHistogramReducer : Reducer<StoreState, SetOnchainHistogram>
    {
        public override StoreState Reduce(StoreState state, SetOnchainHistogram action)
        {
            var data = action.Data ?? state.OnchainHistogram?.Data;
            return state with
            {
                OnchainHistogram = (state.OnchainHistogram ?? new RemoteData<HistogramData>()) with {
                    Data = data,
                    Error = action.Error,
                    Loading = false,
                    Sending = false
                },
                UnifiedHistogram = GetUnifiedHistogram(data, state.LightningHistogram?.Data)
            };
        }
    }

    protected class SetLightningHistogramReducer : Reducer<StoreState, SetLightningHistogram>
    {
        public override StoreState Reduce(StoreState state, SetLightningHistogram action)
        {
            var data = action.Data ?? state.LightningHistogram?.Data;
            return state with
            {
                LightningHistogram = (state.LightningHistogram ?? new RemoteData<HistogramData>()) with {
                    Data = data,
                    Error = action.Error,
                    Loading = false,
                    Sending = false
                },
                UnifiedHistogram = GetUnifiedHistogram(state.OnchainHistogram?.Data, data)
            };
        }
    }

    protected class SetRolesReducer : Reducer<StoreState, SetRoles>
    {
        public override StoreState Reduce(StoreState state, SetRoles action)
        {
            return state with
            {
                Roles = (state.Roles ?? new RemoteData<IEnumerable<RoleData>>()) with
                {
                    Data = action.Roles,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    protected class SetUsersReducer : Reducer<StoreState, SetUsers>
    {
        public override StoreState Reduce(StoreState state, SetUsers action)
        {
            return state with
            {
                Users = (state.Users ?? new RemoteData<IEnumerable<StoreUserData>>()) with
                {
                    Data = action.Users,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    protected class SetNotificationsReducer : Reducer<StoreState, SetNotifications>
    {
        public override StoreState Reduce(StoreState state, SetNotifications action)
        {
            return state with
            {
                Notifications = (state.Notifications ?? new RemoteData<IEnumerable<NotificationData>>()) with
                {
                    Data = action.Notifications,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    protected class SetNotificationReducer : Reducer<StoreState, SetNotification>
    {
        public override StoreState Reduce(StoreState state, SetNotification action)
        {
            if (state.Notifications?.Data == null || action.Notification == null) return state;
            return state with
            {
                Notifications = state.Notifications with
                {
                    Data = state.Notifications.Data.Select(n =>
                        n.Id == action.Notification.Id ? action.Notification : n)
                }
            };
        }
    }

    protected class SetInvoicesReducer : Reducer<StoreState, SetInvoices>
    {
        public override StoreState Reduce(StoreState state, SetInvoices action)
        {
            return state with
            {
                Invoices = (state.Invoices ?? new RemoteData<IEnumerable<InvoiceData>>()) with
                {
                    Data = action.Invoices ?? state.Invoices?.Data,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    protected class SetInvoiceReducer : Reducer<StoreState, SetInvoice>
    {
        public override StoreState Reduce(StoreState state, SetInvoice action)
        {
            var invoice = action.Invoice ?? GetInvoice(state, action.InvoiceId)?.Data;
            if (state._invoicesById.ContainsKey(action.InvoiceId))
                state._invoicesById.Remove(action.InvoiceId);
            return state with
            {
                _invoicesById = new Dictionary<string, RemoteData<InvoiceData>?>(state._invoicesById)
                {
                    { action.InvoiceId, new RemoteData<InvoiceData>(invoice, action.Error, false) }
                }
            };
        }
    }

    protected class SetInvoicePaymentMethodsReducer : Reducer<StoreState, SetInvoicePaymentMethods>
    {
        public override StoreState Reduce(StoreState state, SetInvoicePaymentMethods action)
        {
            var pms = action.PaymentMethods ?? GetInvoicePaymentMethods(state, action.InvoiceId)?.Data;
            if (state._invoicePaymentMethodsById.ContainsKey(action.InvoiceId))
                state._invoicePaymentMethodsById.Remove(action.InvoiceId);
            return state with
            {
                _invoicePaymentMethodsById = new Dictionary<string, RemoteData<InvoicePaymentMethodDataModel[]>?>(state._invoicePaymentMethodsById)
                {
                    { action.InvoiceId, new RemoteData<InvoicePaymentMethodDataModel[]>(pms, action.Error) }
                }
            };
        }
    }

    protected class SetRatesReducer : Reducer<StoreState, SetRates>
    {
        public override StoreState Reduce(StoreState state, SetRates action)
        {
            return state with
            {
                Rates = (state.Rates ?? new RemoteData<IEnumerable<StoreRateResult>>()) with {
                    Data = action.Rates ?? state.Rates?.Data,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    protected class SetPointOfSaleReducer : Reducer<StoreState, SetPointOfSale>
    {
        public override StoreState Reduce(StoreState state, SetPointOfSale action)
        {
            return state with
            {
                PointOfSale = (state.PointOfSale ?? new RemoteData<PointOfSaleAppData>()) with
                {
                    Data = action.AppData ?? state.PointOfSale?.Data,
                    Error = action.Error,
                    Loading = false,
                    Sending = false
                }
            };
        }
    }

    protected class UpdatePointOfSaleReducer : Reducer<StoreState, UpdatePointOfSale>
    {
        public override StoreState Reduce(StoreState state, UpdatePointOfSale action)
        {
            return state with
            {
                PointOfSale = (state.PointOfSale ?? new RemoteData<PointOfSaleAppData>()) with
                {
                    Sending = true
                }
            };
        }
    }

    protected class SetPosItemStatsReducer : Reducer<StoreState, SetPosItemStats>
    {
        public override StoreState Reduce(StoreState state, SetPosItemStats action)
        {
            return state with
            {
                PosItemStats = (state.PosItemStats ?? new RemoteData<List<AppItemStats>>()) with
                {
                    Data = action.ItemStats ?? state.PosItemStats?.Data,
                    Error = action.Error,
                    Loading = false,
                    Sending = false
                }
            };
        }
    }

    protected class SetPosSalesStatsReducer : Reducer<StoreState, SetPosSalesStats>
    {
        public override StoreState Reduce(StoreState state, SetPosSalesStats action)
        {
            return state with
            {
                PosSalesStats = (state.PosSalesStats ?? new RemoteData<AppSalesStats>()) with
                {
                    Data = action.SalesStats ?? state.PosSalesStats?.Data,
                    Error = action.Error,
                    Loading = false,
                    Sending = false
                }
            };
        }
    }

    public RemoteData<InvoiceData>? GetInvoice(string invoiceId)
    {
        if (_invoicesById.TryGetValue(invoiceId, out var invoice)) return invoice;
        var invoiceData = Invoices?.Data?.FirstOrDefault(i => i.Id == invoiceId);
        return invoiceData == null ? null : new RemoteData<InvoiceData>(invoiceData);
    }

    private static RemoteData<InvoiceData>? GetInvoice(StoreState state, string invoiceId)
    {
        return state.GetInvoice(invoiceId);
    }

    public RemoteData<InvoicePaymentMethodDataModel[]>? GetInvoicePaymentMethods(string invoiceId)
    {
        return _invoicePaymentMethodsById.TryGetValue(invoiceId, out var pms) ? pms :null;
    }

    private static RemoteData<InvoicePaymentMethodDataModel[]>? GetInvoicePaymentMethods(StoreState state, string invoiceId)
    {
        return state.GetInvoicePaymentMethods(invoiceId);
    }

    private static HistogramData? GetUnifiedHistogram(HistogramData? onchain, HistogramData? lightning)
    {
        if (onchain == null && lightning == null) return null;
        // if there's only one, return that
        if (onchain == null || lightning == null) return onchain ?? lightning;
        // if types or series differ, return null
        if (onchain.Type != lightning.Type || onchain.Series?.Count != lightning.Series?.Count) return null;
        // merge the two
        var histogram = new HistogramData
        {
            Type = onchain.Type,
            Series = onchain.Series,
            Labels = onchain.Labels,
            Balance = onchain.Balance + lightning.Balance,
        };
        for (var i = 0; i < lightning.Series!.Count; i++) histogram.Series![i] += lightning.Series[i];
        return histogram;
    }

    public class StoreEffects(IState<StoreState> state, IState<UIState> uiState, IAccountManager accountManager)
    {
        [EffectMethod]
        public Task SetStoreInfoEffect(SetStoreInfo action, IDispatcher dispatcher)
        {
            var store = action.StoreInfo;
            if (store != null)
            {
                var storeId = store.Id;
                var posId = store.PosAppId!;
                var histogramType = state.Value.HistogramType ?? uiState.Value.HistogramType;
                dispatcher.Dispatch(new FetchStore(storeId));
                dispatcher.Dispatch(new FetchBalances(storeId, histogramType));
                dispatcher.Dispatch(new FetchNotifications(storeId));
                dispatcher.Dispatch(new FetchRoles(storeId));
                dispatcher.Dispatch(new FetchUsers(storeId));
                dispatcher.Dispatch(new FetchInvoices(storeId));
                dispatcher.Dispatch(new FetchRates(store));
                dispatcher.Dispatch(new FetchPointOfSale(posId));
                dispatcher.Dispatch(new FetchPointOfSaleStats(posId));

                var currency = BitcoinUnits.Contains(store.DefaultCurrency) ? null : store.DefaultCurrency;
                dispatcher.Dispatch(new UIState.SetFiatCurrency(currency));
            }
            return Task.CompletedTask;
        }

        [EffectMethod]
        public async Task FetchStoreEffect(FetchStore action, IDispatcher dispatcher)
        {
            try
            {
                var store = await accountManager.GetClient().GetStore(action.StoreId);
                dispatcher.Dispatch(new SetStore(store, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetStore(null, error));
            }
        }

        [EffectMethod]
        public async Task UpdateStoreEffect(UpdateStore action, IDispatcher dispatcher)
        {
            try
            {
                var store = await accountManager.GetClient().UpdateStore(action.StoreId, action.Request);
                dispatcher.Dispatch(new UpdatedStore(store, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new UpdatedStore(null, error));
            }
        }

        [EffectMethod]
        public Task FetchBalancesEffect(FetchBalances action, IDispatcher dispatcher)
        {
            dispatcher.Dispatch(new FetchOnchainBalance(action.StoreId));
            dispatcher.Dispatch(new FetchLightningBalance(action.StoreId));
            dispatcher.Dispatch(new FetchHistograms(action.StoreId, action.Type));
            return Task.CompletedTask;
        }

        [EffectMethod]
        public Task FetchHistogramsEffect(FetchHistograms action, IDispatcher dispatcher)
        {
            dispatcher.Dispatch(new FetchOnchainHistogram(action.StoreId, action.Type));
            dispatcher.Dispatch(new FetchLightningHistogram(action.StoreId, action.Type));
            return Task.CompletedTask;
        }

        [EffectMethod]
        public async Task FetchOnchainBalanceEffect(FetchOnchainBalance action, IDispatcher dispatcher)
        {
            try
            {
                var overview = await accountManager.GetClient().ShowOnChainWalletOverview(action.StoreId, "BTC");
                dispatcher.Dispatch(new SetOnchainBalance(overview, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetOnchainBalance(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchLightningBalanceEffect(FetchLightningBalance action, IDispatcher dispatcher)
        {
            try
            {
                var balance = await accountManager.GetClient().GetLightningNodeBalance(action.StoreId, "BTC");
                dispatcher.Dispatch(new SetLightningBalance(balance, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetLightningBalance(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchOnchainHistogramEffect(FetchOnchainHistogram action, IDispatcher dispatcher)
        {
            try
            {
                var data = await accountManager.GetClient().GetOnChainWalletHistogram(action.StoreId, "BTC", action.Type);
                dispatcher.Dispatch(new SetOnchainHistogram(data, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetOnchainHistogram(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchLightningHistogramEffect(FetchLightningHistogram action, IDispatcher dispatcher)
        {
            try
            {
                var data = await accountManager.GetClient().GetLightningNodeHistogram(action.StoreId, "BTC", action.Type);
                dispatcher.Dispatch(new SetLightningHistogram(data, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetLightningHistogram(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchRolesEffect(FetchRoles action, IDispatcher dispatcher)
        {
            try
            {
                var roles = await accountManager.GetClient().GetStoreRoles(action.StoreId);
                dispatcher.Dispatch(new SetRoles(roles, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetRoles(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchUsersEffect(FetchUsers action, IDispatcher dispatcher)
        {
            try
            {
                var users = await accountManager.GetClient().GetStoreUsers(action.StoreId);
                dispatcher.Dispatch(new SetUsers(users, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetUsers(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchNotificationsEffect(FetchNotifications action, IDispatcher dispatcher)
        {
            try
            {
                var notifications = await accountManager.GetClient().GetNotifications(storeId: [action.StoreId]);
                dispatcher.Dispatch(new SetNotifications(notifications, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetNotifications(null, error));
            }
        }

        [EffectMethod]
        public async Task UpdateNotificationEffect(UpdateNotification action, IDispatcher dispatcher)
        {
            try
            {
                var notification = await accountManager.GetClient().UpdateNotification(action.NotificationId, action.Seen);
                dispatcher.Dispatch(new SetNotification(notification, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetNotification(null, error));
            }
        }

        [EffectMethod]
        public async Task UpdatePointOfSaleEffect(UpdatePointOfSale action, IDispatcher dispatcher)
        {
            try
            {
                var appData = await accountManager.GetClient().UpdatePointOfSaleApp(action.AppId, action.Request);
                dispatcher.Dispatch(new UpdatedPointOfSale(appData, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new UpdatedPointOfSale(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchPointOfSaleEffect(FetchPointOfSale action, IDispatcher dispatcher)
        {
            try
            {
                var appData = await accountManager.GetClient().GetPosApp(action.AppId);
                dispatcher.Dispatch(new SetPointOfSale(appData, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetPointOfSale(null, error));
            }
        }

        [EffectMethod]
        public Task FetchPointOfSaleStatsEffect(FetchPointOfSaleStats action, IDispatcher dispatcher)
        {
            dispatcher.Dispatch(new FetchPosItemStats(action.AppId));
            dispatcher.Dispatch(new FetchPosSalesStats(action.AppId));
            return Task.CompletedTask;
        }

        [EffectMethod]
        public async Task FetchPosItemStatsEffect(FetchPosItemStats action, IDispatcher dispatcher)
        {
            try
            {
                var data = await accountManager.GetClient().GetAppTopItems(action.AppId);
                dispatcher.Dispatch(new SetPosItemStats(data, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetPosItemStats(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchPosSalesStatsEffect(FetchPosSalesStats action, IDispatcher dispatcher)
        {
            try
            {
                var data = await accountManager.GetClient().GetAppSales(action.AppId);
                dispatcher.Dispatch(new SetPosSalesStats(data, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetPosSalesStats(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchRatesEffect(FetchRates action, IDispatcher dispatcher)
        {
            var currency = action.Store.DefaultCurrency;
            if (BitcoinUnits.Contains(currency)) return;
            try
            {
                var rates = await accountManager.GetClient().GetStoreRates(action.Store.Id, [$"BTC_{currency}"]);
                dispatcher.Dispatch(new SetRates(rates, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetRates(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchInvoicesEffect(FetchInvoices action, IDispatcher dispatcher)
        {
            try
            {
                var invoices = await accountManager.GetClient().GetInvoices(action.StoreId);
                dispatcher.Dispatch(new SetInvoices(invoices, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetInvoices(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchInvoiceEffect(FetchInvoice action, IDispatcher dispatcher)
        {
            try
            {
                var invoice = await accountManager.GetClient().GetInvoice(action.StoreId, action.InvoiceId);
                dispatcher.Dispatch(new SetInvoice(invoice, null, action.InvoiceId));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetInvoice(null, error, action.InvoiceId));
            }
        }

        [EffectMethod]
        public async Task FetchInvoicePaymentMethodsEffect(FetchInvoicePaymentMethods action, IDispatcher dispatcher)
        {
            try
            {
                var pms = await accountManager.GetClient().GetInvoicePaymentMethods(action.StoreId, action.InvoiceId);
                dispatcher.Dispatch(new SetInvoicePaymentMethods(pms, null, action.InvoiceId));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetInvoicePaymentMethods(null, error, action.InvoiceId));
            }
        }

        [EffectMethod]
        public Task SetHistogramTypeEffect(SetHistogramType action, IDispatcher dispatcher)
        {
            var storeInfo = state.Value.StoreInfo;
            if (storeInfo != null)
                dispatcher.Dispatch(new FetchHistograms(storeInfo.Id, action.Type));
            return Task.CompletedTask;
        }
    }
}



