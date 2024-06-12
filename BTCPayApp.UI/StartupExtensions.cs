using BTCPayServer.Client;
using BTCPayServer.Security;
using BTCPayServer.Security.GreenField;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Fluxor;
using Fluxor.Blazor.Web.ReduxDevTools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayApp.UI;

public static class StartupExtensions
{
    public static IServiceCollection AddBTCPayAppUIServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddOptions();
        serviceCollection.AddSingleton<IAuthorizationHandler, BearerAuthorizationHandler>();
        serviceCollection.AddSingleton<DisplayFormatter>();
        serviceCollection.AddSingleton(CurrencyNameTable.Instance);
        serviceCollection.AddAuthorizationCore(options =>
        {
            options.AddBTCPayPolicies();
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
