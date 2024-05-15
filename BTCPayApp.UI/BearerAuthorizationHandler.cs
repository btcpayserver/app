using BTCPayServer;
using BTCPayServer.Client;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;
using StoreData = BTCPayServer.Client.Models.StoreData;

namespace BTCPayApp.UI
{
    public class BearerAuthorizationHandler : AuthorizationHandler<PolicyRequirement>
    {
        private readonly IOptionsMonitor<IdentityOptions> _identityOptions;

        public BearerAuthorizationHandler(IOptionsMonitor<IdentityOptions> identityOptions)
        {
            _identityOptions = identityOptions;
        }

        //TODO: In the future, we will add these store permissions to actual aspnet roles, and remove this class.
        private static readonly PermissionSet _serverAdminRolePermissions = new([Permission.Create(Policies.CanViewStoreSettings)]);

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PolicyRequirement requirement)
        {
            if (context.User.Identity?.AuthenticationType != AuthenticationSchemes.Bearer)
                return;

            var userId = context.User.Claims.FirstOrDefault(c => c.Type == _identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType)?.Value;
            if (string.IsNullOrEmpty(userId))
                return;

            StoreData? store = null;
            var success = false;
            var isAdmin = context.User.IsInRole(Roles.ServerAdmin);
            var storeId = context.Resource as string;
            var policy = requirement.Policy;
            var requiredUnscoped = false;
            if (policy.EndsWith(':'))
            {
                policy = policy.Substring(0, policy.Length - 1);
                requiredUnscoped = true;
            }

            if (Policies.IsServerPolicy(policy) && isAdmin)
            {
                success = true;
            }
            else if (Policies.IsUserPolicy(policy) && userId is not null)
            {
                success = true;
            }
            else if (Policies.IsStorePolicy(policy))
            {
                if (isAdmin && storeId is not null)
                {
                    success = _serverAdminRolePermissions.HasPermission(policy, storeId);
                }

                /*if (!success && store?.HasPermission(userId, policy) is true)
                {
                    success = true;
                }*/

                if (!success && store is null && requiredUnscoped)
                {
                    success = true;
                }
            }
            if (success)
            {
                context.Succeed(requirement);
            }
        }
    }
}
