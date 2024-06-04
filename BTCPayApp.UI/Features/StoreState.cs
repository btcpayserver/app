using BTCPayApp.CommonServer.Models;
using BTCPayApp.Core.Auth;
using BTCPayServer.Client.Models;
using Fluxor;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record StoreState(
    AppUserStoreInfo? StoreInfo,
    RemoteData<IEnumerable<InvoiceData>>? Invoices)
{
    public AppUserStoreInfo? StoreInfo = StoreInfo;
    public RemoteData<IEnumerable<InvoiceData>>? Invoices = Invoices;

    public StoreState() : this(null, new RemoteData<IEnumerable<InvoiceData>>(null))
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

    public class StoreEffects(IAccountManager accountManager)
    {
        [EffectMethod]
        public Task SetStoreInfoEffect(SetStoreInfo action, IDispatcher dispatcher)
        {
            var store = action.StoreInfo;
            if (store != null)
            {
                dispatcher.Dispatch(new FetchInvoices(store.Id));
            }
            return Task.CompletedTask;
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



