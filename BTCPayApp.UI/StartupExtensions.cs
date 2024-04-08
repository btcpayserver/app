using BTCPayApp.Core;
using Fluxor;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
namespace BTCPayApp.UI;

public static class StartupExtensions
{
    public static IServiceCollection AddBTCPayAppUIServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddOptions();
        serviceCollection.AddAuthorizationCore();
        serviceCollection.AddCascadingAuthenticationState();
        serviceCollection.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
        serviceCollection.AddScoped<BTCPayServerAppApiClient>();
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
