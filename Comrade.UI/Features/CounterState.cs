using Fluxor;

namespace Comrade.UI.Features;

[FeatureState]
public record CounterState(int ClickCount, bool Loading = false)
{
    public CounterState() : this(0)
    {
    }

    public record IncrementCounterAction(int IncrementBy = 1);

    protected record IncrementedCounterAction(int IncrementBy);

    public record LoadingAction(bool Loading);

    protected class IncrementCounterActionEffect : Effect<IncrementCounterAction>
    {
        public override async Task HandleAsync(IncrementCounterAction action, IDispatcher dispatcher)
        {
            dispatcher.Dispatch(new LoadingAction(true));
            await Task.Delay(1000);

            dispatcher.Dispatch(new IncrementedCounterAction(action.IncrementBy));
        }
    }

    protected class IncrementedCounterActionReducer : Reducer<CounterState, IncrementedCounterAction>
    {
        public override CounterState Reduce(CounterState state, IncrementedCounterAction action)
        {
            return new CounterState(state.ClickCount + action.IncrementBy);
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
