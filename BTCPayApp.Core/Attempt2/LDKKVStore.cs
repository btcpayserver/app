﻿using BTCPayApp.Core.Contracts;
using org.ldk.enums;
using org.ldk.structs;

namespace BTCPayApp.Core.Attempt2;

public class LDKKVStore:KVStoreInterface
{
    private readonly IConfigProvider _configProvider;

    public LDKKVStore(IConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }

    public Result_CVec_u8ZIOErrorZ read(string primary_namespace, string secondary_namespace, string key)
    {
        var key1 = $"{primary_namespace}:{secondary_namespace}:{key}";
        var result = _configProvider.Get<byte[]>(key1).ConfigureAwait(false).GetAwaiter().GetResult();
        return result == null ? Result_CVec_u8ZIOErrorZ.err(IOError.LDKIOError_NotFound) : Result_CVec_u8ZIOErrorZ.ok(result);
    }

    public Result_NoneIOErrorZ write(string primary_namespace, string secondary_namespace, string key, byte[] buf)
    {
        var key1 = $"{primary_namespace}:{secondary_namespace}:{key}";
        _configProvider.Set(key1, buf).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }

    public Result_NoneIOErrorZ remove(string primary_namespace, string secondary_namespace, string key, bool lazy)
    {
        var key1 = $"{primary_namespace}:{secondary_namespace}:{key}";
        _configProvider.Set<byte[]>(key1, null).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }

    public Result_CVec_StrZIOErrorZ list(string primary_namespace, string secondary_namespace)
    {
        var key1 = $"{primary_namespace}:{secondary_namespace}:";
        var result = _configProvider.List(key1).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_CVec_StrZIOErrorZ.ok(result.ToArray());
    }
}