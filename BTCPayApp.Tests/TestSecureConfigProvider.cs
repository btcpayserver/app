using System.Collections.Concurrent;
using BTCPayApp.Core.Contracts;
using NBitcoin;

namespace BTCPayApp.Tests;

public class TestSecureConfigProvider : ISecureConfigProvider
{
    private readonly ConcurrentDictionary<string, object> _data = new();

    public Task<T?> Get<T>(string key)
    {
        return _data.TryGetValue(key, out var value)
            ? Task.FromResult((T?) value)
            : Task.FromResult<T?>(default);
    }

    public Task Set<T>(string key, T? value)
    {
        if (value is null)
            _data.TryRemove(key, out _);
        else
            _data.AddOrReplace(key, value);
        return Task.CompletedTask;
    }
}
