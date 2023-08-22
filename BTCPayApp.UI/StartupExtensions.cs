using BTCPayApp.Core.Contracts;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor.Services;

namespace BTCPayApp.UI;

public static class StartupExtensions
{
    public static IServiceCollection AddBTCPayAppUIServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddMudServices();
        serviceCollection.AddFluxor(options =>
        {
            options.UseRouting();
            options.ScanAssemblies(typeof(App).Assembly);
#if DEBUG
            options.UseReduxDevTools();
#endif
            options.AddMiddleware<StateMiddleware>();
        });
        serviceCollection.TryAddScoped<ISystemThemeProvider, MudBlazorThemeProvider>();
        return serviceCollection;
    }
}