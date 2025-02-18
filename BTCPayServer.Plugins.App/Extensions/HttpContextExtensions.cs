using System;
using System.Linq;
using BTCPayServer.Client;
using BTCPayServer.Security.Greenfield;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Plugins.App.Extensions;

// Adapted from GreenFieldAuthorizationHandler.cs
public static class HttpContextExtensions
{
    public static string[] GetPermissions(this HttpContext context)
    {
        return context.User.Claims.Where(c =>
                c.Type.Equals(GreenfieldConstants.ClaimTypes.Permission, StringComparison.InvariantCultureIgnoreCase))
            .Select(claim => claim.Value).ToArray();
    }

    public static bool HasPermission(this HttpContext context, Permission permission)
    {
        foreach (var claim in context.User.Claims.Where(c =>
                     c.Type.Equals(GreenfieldConstants.ClaimTypes.Permission, StringComparison.InvariantCultureIgnoreCase)))
        {
            if (Permission.TryParse(claim.Value, out var claimPermission))
            {
                if (claimPermission.Contains(permission))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
