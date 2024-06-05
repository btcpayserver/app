using BTCPayApp.CommonServer.Models;
using BTCPayApp.Core.Auth;
using BTCPayServer.Client.Models;
using Fluxor;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record StoreState(
    AppUserStoreInfo? StoreInfo,
    RemoteData<PointOfSaleAppData>? PointOfSale,
    RemoteData<IEnumerable<InvoiceData>>? Invoices)
{
    public AppUserStoreInfo? StoreInfo = StoreInfo;
    public RemoteData<PointOfSaleAppData>? PointOfSale = PointOfSale;
    public RemoteData<IEnumerable<InvoiceData>>? Invoices = Invoices;


    public StoreState() : this(null, null, null)
    {
    }


    public record SetStoreInfo(AppUserStoreInfo? StoreInfo);

    protected class SetStoreInfoReducer : Reducer<StoreState, SetStoreInfo>
    {
        public override StoreState Reduce(StoreState state, SetStoreInfo action)
        {
            return state with
            {
                StoreInfo = action.StoreInfo,
                PointOfSale = new RemoteData<PointOfSaleAppData>(null),
                Invoices = new RemoteData<IEnumerable<InvoiceData>>(null)
            };
        }
    }

    public record FetchInvoices(string? StoreId);

    protected class FetchInvoicesReducer : Reducer<StoreState, FetchInvoices>
    {
        public override StoreState Reduce(StoreState state, FetchInvoices action)
        {
            return state with
            {
                Invoices = new RemoteData<IEnumerable<InvoiceData>>(state.Invoices?.Data, true)
            };
        }
    }

    protected record FetchedInvoices(IEnumerable<InvoiceData>? Invoices, string? Error);

    protected class FetchedInvoicesReducer : Reducer<StoreState, FetchedInvoices>
    {
        public override StoreState Reduce(StoreState state, FetchedInvoices action)
        {
            var invoices = action.Invoices ?? state.Invoices?.Data;
            return state with
            {
                Invoices = new RemoteData<IEnumerable<InvoiceData>>(invoices, false, action.Error)
            };
        }
    }

    public record FetchPointOfSale(string? AppId);

    protected class FetchPointOfSaleReducer : Reducer<StoreState, FetchPointOfSale>
    {
        public override StoreState Reduce(StoreState state, FetchPointOfSale action)
        {
            return state with
            {
                PointOfSale = new RemoteData<PointOfSaleAppData>(state.PointOfSale?.Data, true)
            };
        }
    }

    protected record FetchedPointOfSale(PointOfSaleAppData? AppData, string? Error);

    protected class FetchedPointOfSaleReducer : Reducer<StoreState, FetchedPointOfSale>
    {
        public override StoreState Reduce(StoreState state, FetchedPointOfSale action)
        {
            var appData = action.AppData ?? state.PointOfSale?.Data;
            return state with
            {
                PointOfSale = new RemoteData<PointOfSaleAppData>(appData, false, action.Error)
            };
        }
    }

    public class StoreEffects(IAccountManager accountManager)
    {
        [EffectMethod]
        public Task SetStoreInfoEffect(SetStoreInfo action, IDispatcher dispatcher)
        {
            var store = action.StoreInfo;
            if (store != null)
            {
                dispatcher.Dispatch(new FetchInvoices(store.Id));
                dispatcher.Dispatch(new FetchPointOfSale(store.PosAppId));
            }
            return Task.CompletedTask;
        }

        [EffectMethod]
        public async Task FetchPointOfSaleEffect(FetchPointOfSale action, IDispatcher dispatcher)
        {
            try
            {
                var appData = await accountManager.GetClient().GetPosApp(action.AppId);
                dispatcher.Dispatch(new FetchedPointOfSale(appData, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new FetchedPointOfSale(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchInvoicesEffect(FetchInvoices action, IDispatcher dispatcher)
        {
            try
            {
                var invoices = await accountManager.GetClient().GetInvoices(action.StoreId);
                dispatcher.Dispatch(new FetchedInvoices(invoices, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new FetchedInvoices(null, error));
            }
        }
    }
}



