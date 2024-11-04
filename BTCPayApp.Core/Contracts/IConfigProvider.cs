using BTCPayApp.Core.Helpers;

namespace BTCPayApp.Core.Contracts;

public abstract class ConfigProvider : IDisposable
{
    public abstract Task<T?> Get<T>(string key);
    public abstract Task Set<T>(string key, T? value, bool backup);
    public abstract Task<IEnumerable<string>> List(string prefix);
    public AsyncEventHandler<string>? Updated;

    public virtual void Dispose()
    {
        Updated = null;
    }
}