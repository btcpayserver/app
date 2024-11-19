using Microsoft.AspNetCore.Authorization;

namespace BTCPayApp.Core.Helpers;

// Copied from BTCPayServer
public class PolicyRequirement : IAuthorizationRequirement
{
    public PolicyRequirement(string policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Policy = policy;
    }
    public string Policy { get; }
}
