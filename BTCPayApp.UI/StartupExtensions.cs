using BTCPayServer.Client;
using BTCPayServer.Security;
using BTCPayServer.Security.GreenField;
using Fluxor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayApp.UI;

public static class StartupExtensions
{
    public static IServiceCollection AddBTCPayAppUIServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddOptions();
        serviceCollection.AddSingleton<IAuthorizationHandler, BearerAuthorizationHandler>();
        serviceCollection.AddAuthorizationCore(options =>
        {
            foreach (var policy in Policies.AllPolicies)
                options.AddPolicy(policy);
            options.AddPolicy(Policies.CanModifyStoreSettingsUnscoped);
            options.AddPolicy(ServerPolicies.CanGetRates.Key);
        });
        serviceCollection.AddCascadingAuthenticationState();
        serviceCollection.AddFluxor(options =>
        {
            options.UseRouting();
            options.ScanAssemblies(typeof(App).Assembly);
#if DEBUG
            options.UseReduxDevTools();
#endif
            options.AddMiddleware<StateMiddleware>();
        });
        return serviceCollection;
    }
}
