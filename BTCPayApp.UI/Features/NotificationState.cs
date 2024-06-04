using BTCPayApp.Core.Auth;
using BTCPayServer.Client.Models;
using Fluxor;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record NotificationState(
    RemoteData<IEnumerable<NotificationData>>? Notifications)
{
    public RemoteData<IEnumerable<NotificationData>>? Notifications = Notifications;

    public NotificationState() : this(new RemoteData<IEnumerable<NotificationData>>(null))
    {
    }

    public record FetchNotifications();

    protected class FetchNotificationsReducer : Reducer<NotificationState, FetchNotifications>
    {
        public override NotificationState Reduce(NotificationState state, FetchNotifications action)
        {
            return state with
            {
                Notifications = new RemoteData<IEnumerable<NotificationData>>(state.Notifications?.Data, true)
            };
        }
    }

    protected record FetchedNotifications(IEnumerable<NotificationData>? Notifications, string? Error);

    protected class FetchedNotificationsReducer : Reducer<NotificationState, FetchedNotifications>
    {
        public override NotificationState Reduce(NotificationState state, FetchedNotifications action)
        {
            var notifications = action.Notifications ?? state.Notifications?.Data;
            return state with
            {
                Notifications = new RemoteData<IEnumerable<NotificationData>>(notifications, false, action.Error)
            };
        }
    }

    public class NotificationEffects(IAccountManager accountManager)
    {
        [EffectMethod]
        public async Task FetchNotificationsEffect(FetchNotifications action, IDispatcher dispatcher)
        {
            try
            {
                var notifications = await accountManager.GetClient().GetNotifications();
                dispatcher.Dispatch(new FetchedNotifications(notifications, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new FetchedNotifications(null, error));
            }
        }
    }
}



