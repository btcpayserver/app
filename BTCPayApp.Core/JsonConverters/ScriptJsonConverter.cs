using NBitcoin;

namespace BTCPayApp.Core.JsonConverters;

public class ScriptJsonConverter : GenericStringJsonConverter<Script>
{
    public override Script Create(string str)
    {
        return new Script(str);
    }
}