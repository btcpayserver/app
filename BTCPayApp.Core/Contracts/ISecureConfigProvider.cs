namespace BTCPayApp.Core.Contracts;

public interface ISecureConfigProvider

{
    Task<T?> Get<T>(string key);
    Task Set<T>(string key, T? value);
}