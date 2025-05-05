using BTCPayApp.Core.Contracts;
using org.ldk.enums;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKKVStore(ConfigProvider configProvider) : KVStoreInterface
{
    private string CombineKey(string primary_namespace, string secondary_namespace, string key)
    {
        var str = "ln:";
        if (!string.IsNullOrEmpty(primary_namespace))
        {
            str += primary_namespace + ":";
        }
        if (!string.IsNullOrEmpty(secondary_namespace))
        {
            str += secondary_namespace + ":";
        }

        if (!string.IsNullOrEmpty(key))
        {
            str += key;
        }

        return str;
    }
    public Result_CVec_u8ZIOErrorZ read(string primary_namespace, string secondary_namespace, string key)
    {
        var key1 = CombineKey(primary_namespace, secondary_namespace, key);
        var result = configProvider.Get<byte[]>(key1).ConfigureAwait(false).GetAwaiter().GetResult();
        return result == null ? Result_CVec_u8ZIOErrorZ.err(IOError.LDKIOError_NotFound) : Result_CVec_u8ZIOErrorZ.ok(result);
    }

    public Result_NoneIOErrorZ write(string primary_namespace, string secondary_namespace, string key, byte[] buf)
    {
        var key1 = CombineKey(primary_namespace, secondary_namespace, key);
        configProvider.Set(key1, buf, true).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }

    public Result_NoneIOErrorZ remove(string primary_namespace, string secondary_namespace, string key, bool lazy)
    {
        var key1 = CombineKey(primary_namespace, secondary_namespace, key);
        configProvider.Set<byte[]>(key1, null, true).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }

    public Result_CVec_StrZIOErrorZ list(string primary_namespace, string secondary_namespace)
    {
        var key1 = CombineKey(primary_namespace, secondary_namespace, string.Empty);
        var result = configProvider.List(key1).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_CVec_StrZIOErrorZ.ok(result.ToArray());
    }
}
