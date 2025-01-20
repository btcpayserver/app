using System.Net;
using BTCPayApp.Core.Helpers;
using NBitcoin;

namespace BTCPayApp.Core.JsonConverters;

public class UInt256JsonConverter : GenericStringJsonConverter<uint256>
{
    public override uint256 Create(string str)
    {
        return uint256.Parse(str);
    }
}

public class EndPointJsonConverter : GenericStringJsonConverter<EndPoint?>
{
    public override EndPoint? Create(string str)
    {
        if (string.IsNullOrEmpty(str)) return null;
        if (EndPointParser.TryParse(str, 9735, out var endpoint)) return endpoint;
        throw new FormatException("Invalid endpoint");
    }

    public override string? ToString(EndPoint? value)
    {
        return value?.ToEndpointString();
    }
}
