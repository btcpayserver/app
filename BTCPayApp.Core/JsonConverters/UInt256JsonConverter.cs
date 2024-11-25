using NBitcoin;

namespace BTCPayApp.Core.JsonConverters;

public class UInt256JsonConverter : GenericStringJsonConverter<uint256>
{
    public override uint256 Create(string str)
    {
        return uint256.Parse(str);
    }
}