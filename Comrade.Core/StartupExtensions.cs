using Microsoft.Extensions.DependencyInjection;

namespace Comrade.Core;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureComradeCore(this IServiceCollection serviceCollection)
    {
        return serviceCollection;
    }
}