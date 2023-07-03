using System.Text.Json.Serialization;
using BTCPayApp.CommonServer;
using NBitcoin;

namespace BTCPayApp.Core;


public static class Extensions
{
    
    public static  Network? ParseNetwork(this PairSuccessResult pairSuccessResult)
    {
        return NBitcoin.Network.GetNetwork(pairSuccessResult.Network);
    }
}