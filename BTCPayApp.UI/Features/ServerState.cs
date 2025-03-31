using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Models;
using BTCPayApp.UI.Util;
using BTCPayServer.Client.Models;
using Fluxor;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record ServerState
{
    public RemoteData<IEnumerable<ApplicationUserData>>? Users;
    public RemoteData<IEnumerable<RoleData>>? Roles;

    public record FetchRoles();
    public record FetchUsers();
    public record SetRoles(IEnumerable<RoleData>? Roles, string? Error);
    public record SetUsers(IEnumerable<ApplicationUserData>? Users, string? Error);

    protected class FetchRolesReducer : Reducer<ServerState, FetchRoles>
    {
        public override ServerState Reduce(ServerState state, FetchRoles action)
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

    protected class FetchUsersReducer : Reducer<ServerState, FetchUsers>
    {
        public override ServerState Reduce(ServerState state, FetchUsers action)
        {
            return state with
            {
                Users = (state.Users ?? new RemoteData<IEnumerable<ApplicationUserData>>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class SetRolesReducer : Reducer<ServerState, SetRoles>
    {
        public override ServerState Reduce(ServerState state, SetRoles action)
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

    protected class SetUsersReducer : Reducer<ServerState, SetUsers>
    {
        public override ServerState Reduce(ServerState state, SetUsers action)
        {
            return state with
            {
                Users = (state.Users ?? new RemoteData<IEnumerable<ApplicationUserData>>()) with
                {
                    Data = action.Users,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    public class ServerEffects(IState<ServerState> state, IAccountManager accountManager)
    {
        [EffectMethod]
        public async Task FetchRolesEffect(FetchRoles action, IDispatcher dispatcher)
        {
            try
            {
                var roles = await accountManager.GetClient().GetServerRoles();
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
                var users = await accountManager.GetClient().GetUsers();
                dispatcher.Dispatch(new SetUsers(users, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetUsers(null, error));
            }
        }
    }
}



