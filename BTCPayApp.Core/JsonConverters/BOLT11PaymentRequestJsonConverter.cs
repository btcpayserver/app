using BTCPayServer.Lightning;

namespace BTCPayApp.Core.JsonConverters;

public class BOLT11PaymentRequestJsonConverter : GenericStringJsonConverter<BOLT11PaymentRequest>
{
    public override BOLT11PaymentRequest Create(string str)
    {
        return NetworkHelper.Try(network => BOLT11PaymentRequest.Parse(str, network));
    }
}