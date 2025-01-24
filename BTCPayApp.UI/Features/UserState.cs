using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Models;
using BTCPayServer.Client.Models;
using Fluxor;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record UserState
{
    public RemoteData<AppUserInfo>? Info;

    public record FetchInfo;
    public record SetInfo(AppUserInfo? Info, string? Error);
    public record UpdateUser(UpdateApplicationUserRequest Request);
    public record UpdatedUser(ApplicationUserData? UserData, string? Error);

    protected class FetchInfoReducer : Reducer<UserState, FetchInfo>
    {
        public override UserState Reduce(UserState state, FetchInfo action)
        {
            return state with
            {
                Info = (state.Info ?? new RemoteData<AppUserInfo>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class SetInfoReducer : Reducer<UserState, SetInfo>
    {
        public override UserState Reduce(UserState state, SetInfo action)
        {
            return state with
            {
                Info = (state.Info ?? new RemoteData<AppUserInfo>()) with
                {
                    Data = action.Info,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    protected class UpdateUserReducer : Reducer<UserState, UpdateUser>
    {
        public override UserState Reduce(UserState state, UpdateUser action)
        {
            return state with
            {
                Info = (state.Info ?? new RemoteData<AppUserInfo>()) with
                {
                    Sending = true
                }
            };
        }
    }

    protected class UpdatedUserReducer : Reducer<UserState, UpdatedUser>
    {
        public override UserState Reduce(UserState state, UpdatedUser action)
        {
            var newState = state with
            {
                Info = state.Info! with
                {
                    Error = action.Error,
                    Sending = false
                }
            };
            if (action.UserData != null && newState.Info.Data != null)
            {
                newState.Info.Data.Name = action.UserData.Name;
                newState.Info.Data.Email = action.UserData.Email;
                newState.Info.Data.ImageUrl = action.UserData.ImageUrl;
            }
            return newState;
        }
    }

    public class UserEffects(IAccountManager accountManager)
    {
        [EffectMethod]
        public async Task FetchInfoEffect(FetchInfo action, IDispatcher dispatcher)
        {
            try
            {
                var info = await accountManager.GetClient().GetUserInfo();
                dispatcher.Dispatch(new SetInfo(info, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetInfo(null, error));
            }
        }

        [EffectMethod]
        public async Task UpdateUserEffect(UpdateUser action, IDispatcher dispatcher)
        {
            try
            {
                var userData = await accountManager.GetClient().UpdateCurrentUser(action.Request);
                dispatcher.Dispatch(new UpdatedUser(userData, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new UpdatedUser(null, error));
            }
        }
    }
}



