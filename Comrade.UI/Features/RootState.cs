using Comrade.Core;
using Comrade.UI.Pages;
using Fluxor;
using Fluxor.Blazor.Web.Middlewares.Routing;
using Microsoft.AspNetCore.Components;

namespace Comrade.UI.Features;

[FeatureState]
public record RootState( bool Loading)
{
    public RootState() : this(true)
    {
    }

    public record ConfigLoadedAction(EssentialConfig? Config);


    public record LoadingAction(bool Loading);

    protected class ConfigLoadedActionEffect : Effect<ConfigLoadedAction>
    {
        private readonly NavigationManager _navigationManager;

        public ConfigLoadedActionEffect(NavigationManager navigationManager)
        {
            _navigationManager = navigationManager;
        }
        public override async Task HandleAsync(ConfigLoadedAction action, IDispatcher dispatcher)
        {
            if (action.Config?.PairingResult is null && !_navigationManager.Uri.Contains("pair"))
                dispatcher.Dispatch(new GoAction(Routes.FirstRun));
            dispatcher.Dispatch(new LoadingAction(false));
        }
    }

    protected class LoadingCounterActionReducer : Reducer<CounterState, LoadingAction>
    {
        public override CounterState Reduce(CounterState state, LoadingAction action)
        {
            return state with { Loading = action.Loading };
        }
    }
}