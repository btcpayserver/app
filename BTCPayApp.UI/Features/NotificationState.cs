using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Data;
using BTCPayServer.Client.Models;
using Fluxor;
using Fluxor.Blazor.Web.Middlewares.Routing;
using Microsoft.AspNetCore.Components;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record NotificationState
{
    public RemoteData<IEnumerable<NotificationData>>? Notifications;
    public RemoteData<NotificationSettingsData>? Settings;

    public record FetchNotifications;
    public record FetchedNotifications(IEnumerable<NotificationData>? Notifications, string? Error);
    public record FetchSettings;
    public record FetchedSettings(NotificationSettingsData? Settings, string? Error);
    public record UpdateSettings(UpdateNotificationSettingsRequest Request);
    public record UpdatedSettings(NotificationSettingsData? Settings, string? Error);

    protected class FetchNotificationsReducer : Reducer<NotificationState, FetchNotifications>
    {
        public override NotificationState Reduce(NotificationState state, FetchNotifications action)
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

    protected class FetchedNotificationsReducer : Reducer<NotificationState, FetchedNotifications>
    {
        public override NotificationState Reduce(NotificationState state, FetchedNotifications action)
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

    protected class FetchSettingsReducer : Reducer<NotificationState, FetchSettings>
    {
        public override NotificationState Reduce(NotificationState state, FetchSettings action)
        {
            return state with
            {
                Settings = (state.Settings ?? new RemoteData<NotificationSettingsData>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchedSettingsReducer : Reducer<NotificationState, FetchedSettings>
    {
        public override NotificationState Reduce(NotificationState state, FetchedSettings action)
        {
            return state with
            {
                Settings = (state.Settings ?? new RemoteData<NotificationSettingsData>()) with
                {
                    Data = action.Settings,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    protected class UpdateSettingsReducer : Reducer<NotificationState, UpdateSettings>
    {
        public override NotificationState Reduce(NotificationState state, UpdateSettings action)
        {
            return state with
            {
                Settings = (state.Settings ?? new RemoteData<NotificationSettingsData>()) with
                {
                    Sending = true
                }
            };
        }
    }

    protected class UpdatedSettingsReducer : Reducer<NotificationState, UpdatedSettings>
    {
        public override NotificationState Reduce(NotificationState state, UpdatedSettings action)
        {
            return state with
            {
                Settings = (state.Settings ?? new RemoteData<NotificationSettingsData>()) with
                {
                    Data = action.Settings,
                    Error = action.Error,
                    Sending = false
                }
            };
        }
    }


    public class ConnectionEffects()
    {
        [EffectMethod]
        public async  Task HandleConnectionStateUpdatedAction(RootState.ConnectionStateUpdatedAction action, IDispatcher dispatcher)
        {
            if(action.State == BTCPayConnectionState.WaitingForEncryptionKey)
            {
                dispatcher.Dispatch(new GoAction(Routes.EncryptionKey));
            }
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

        [EffectMethod]
        public async Task FetchSettingsEffect(FetchSettings action, IDispatcher dispatcher)
        {
            try
            {
                var settings = await accountManager.GetClient().GetNotificationSettings();
                dispatcher.Dispatch(new FetchedSettings(settings, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new FetchedSettings(null, error));
            }
        }

        [EffectMethod]
        public async Task UpdateSettingsEffect(UpdateSettings action, IDispatcher dispatcher)
        {
            try
            {
                var settings = await accountManager.GetClient().UpdateNotificationSettings(action.Request);
                dispatcher.Dispatch(new UpdatedSettings(settings, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new UpdatedSettings(null, error));
            }
        }
    }
}



