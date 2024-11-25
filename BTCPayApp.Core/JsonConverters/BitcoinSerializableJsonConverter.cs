using NBitcoin;

namespace BTCPayApp.Core.JsonConverters;

public class BitcoinSerializableJsonConverter<T> : GenericStringJsonConverter<T> where T : IBitcoinSerializable
{
    public override T Create(string str)
    {
        var bytes = Convert.FromHexString(str);
        var instance = Activator.CreateInstance<T>();
        return NetworkHelper.Try(network =>
        {
            instance.ReadWrite(bytes, network);
            return instance;
        });
    }


    public override string ToString(T? instance)
    {
        return Convert.ToHexString(instance.ToBytes()).ToLowerInvariant();
    }
}