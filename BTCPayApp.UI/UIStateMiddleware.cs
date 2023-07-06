using BTCPayApp.Core.Contracts;
using BTCPayApp.UI.Features;
using Fluxor;

namespace BTCPayApp.UI;

public class UIStateMiddleware : Fluxor.Middleware
{
    private readonly IConfigProvider _configProvider;

    public UIStateMiddleware(IConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }
    public override async Task InitializeAsync(IDispatcher dispatcher, IStore store)
    {
        if (store.Features.TryGetValue(typeof(UIState).FullName, out var uiStateFeature))
        {
            var existing = await _configProvider.Get<UIState>("uistate");
            if (existing is not null)
            {
                uiStateFeature.RestoreState(existing);
            }
            uiStateFeature.StateChanged += async (sender, args) =>
            {
                await _configProvider.Set("uistate", (UIState)uiStateFeature.GetState());
            };
        }
        await base.InitializeAsync(dispatcher, store);
            
    }
}