using BTCPayApp.Core.Services;
using BTCPayApp.UI.Util;
using Fluxor;
using Fluxor.Blazor.Web.ReduxDevTools;
using Microsoft.Extensions.DependencyInjection;
using Plk.Blazor.DragDrop;

namespace BTCPayApp.UI;

public static class StartupExtensions
{
    public static IServiceCollection AddBTCPayAppUIServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddOptions();
        serviceCollection.AddSingleton<DisplayFormatter>();
        serviceCollection.AddBlazorDragDrop();
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
