using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Comrade.UI;

public static class StartupExtensions
{
    public static IServiceCollection AddComradeUIServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddMudServices();
        serviceCollection.AddFluxor(options =>
        {
            options.UseRouting();
            options.ScanAssemblies(typeof(App).Assembly);
#if DEBUG
            options.UseReduxDevTools();
#endif
        });

        return serviceCollection;
    }
}
