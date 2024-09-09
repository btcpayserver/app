using System.Collections.Concurrent;
using BTCPayApp.Core.Contracts;
using NBitcoin;

namespace BTCPayApp.Tests;

public class TestSecureConfigProvider : ISecureConfigProvider
{
    private readonly ConcurrentDictionary<string, object> _data = new();

    public async Task<T?> Get<T>(string key)
    {
        if (_data.TryGetValue(key, out var value)) return (T?) value;

        return default;
    }

    public async Task Set<T>(string key, T? value)
    {
        if (value is null)
            _data.TryRemove(key, out _);
        else
            _data.AddOrReplace(key, value);
    }
}