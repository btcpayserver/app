﻿using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Core.Attempt2;

public static class ConfigHelpers
{
    public static async Task<T> GetOrSet<T>(this ISecureConfigProvider secureConfigProvider, string key,
        Func<Task<T>> factory)
    {
        var value = await secureConfigProvider.Get<T>(key);
        if (Equals(value, default(T)))
        {
            value = await factory();
            await secureConfigProvider.Set(key, value);
        }

        return value;
    }

    public static async Task<T> GetOrSet<T>(this IConfigProvider configProvider, string key, Func<Task<T>> factory,
        bool backup)
    {
        var value = await configProvider.Get<T>(key);
        if (Equals(value, default(T)))
        {
            value = await factory();
            await configProvider.Set(key, value, backup);
        }

        return value;
    }
}