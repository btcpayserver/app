using NBitcoin;

namespace BTCPayApp.Core.JsonConverters;

public static class NetworkHelper
{
    public static T Try<T>(Func<Network, T> func)
    {
        Exception? lastException = null;
        foreach (var network in Network.GetNetworks())
            try
            {
                return func.Invoke(network);
            }
            catch (Exception e)
            {
                lastException = e;
            }

        throw lastException!;
    }
}