using Fluxor;
using Havit.Blazor.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Comrade.UI;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureUIServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddHxServices();
        serviceCollection.AddHxMessenger();
        serviceCollection.AddHxMessageBoxHost();
        serviceCollection.AddFluxor(options =>
        {
            options.ScanAssemblies(typeof(App).Assembly);
#if DEBUG
            options.UseReduxDevTools();
#endif
        });

        return serviceCollection;
    }
}