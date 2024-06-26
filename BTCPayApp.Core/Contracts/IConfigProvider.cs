﻿namespace BTCPayApp.Core.Contracts;

public interface IConfigProvider
{
    Task<T?> Get<T>(string key);
    Task Set<T>(string key, T? value);
    Task<IEnumerable<string>> List(string prefix);
}