using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Maui.Services;

public class MauiDataDirectoryProvider: IDataDirectoryProvider
{
    public Task<string> GetAppDataDirectory()
    {
       return Task.FromResult(FileSystem.Current.AppDataDirectory);
    }
    public Task<string> GetCacheDirectory()
    {
        return Task.FromResult(FileSystem.Current.CacheDirectory);
    }
}
