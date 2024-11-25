using NBitcoin;

namespace BTCPayApp.Core.JsonConverters;

public class KeyPathJsonConverter : GenericStringJsonConverter<KeyPath>
{
    public override KeyPath Create(string str)
    {
        return new KeyPath(str);
    }
}