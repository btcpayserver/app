using BTCPayServer.Lightning;

namespace BTCPayApp.Core.JsonConverters;

public class LightMoneyJsonConverter : GenericStringJsonConverter<LightMoney>
{
    public override LightMoney Create(string str)
    {
        return LightMoney.Parse(str);
    }
}