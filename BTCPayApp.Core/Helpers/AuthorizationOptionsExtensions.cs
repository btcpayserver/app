using BTCPayApp.Core.Auth;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayApp.Core.Helpers;

// Copied from BTCPayServer
public static class AuthorizationOptionsExtensions
{
    public static AuthorizationOptions AddPolicies(this AuthorizationOptions options)
    {
        // BTCPay policies
        foreach (var p in Policies.AllPolicies)
        {
            options.AddPolicy(p);
        }
        options.AddPolicy(Policies.CanModifyStoreSettingsUnscoped);
        options.AddPolicy(CanGetRates.Key);
        // app policies
        foreach (var p in AppPolicies.AllPolicies)
        {
            options.AddPolicy(p);
        }
        return options;
    }

    private static void AddPolicy(this AuthorizationOptions options, string policy)
    {
        options.AddPolicy(policy, o => o.AddRequirements(new PolicyRequirement(policy)));
    }

    private class CanGetRates
    {
        public const string Key = "btcpay.store.cangetrates";
    }
}
