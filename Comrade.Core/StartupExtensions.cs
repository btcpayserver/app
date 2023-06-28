using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Comrade.Core;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureComradeCore(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<ComradeConfigManager>();
        serviceCollection.AddSingleton<IHostedService, ComradeConfigManager>(provider =>
            provider.GetRequiredService<ComradeConfigManager>());
        return serviceCollection;
    }
}