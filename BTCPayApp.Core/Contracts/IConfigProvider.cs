namespace BTCPayApp.Core.Contracts;

public interface IConfigProvider
{
    Task<T?> Get<T>(string key);
    Task Set<T>(string key, T? value, bool backup);
    Task<IEnumerable<string>> List(string prefix);
}