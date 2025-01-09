using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Maui.Services;

public class MauiDataDirectoryProvider: IDataDirectoryProvider
{
    public Task<string> GetAppDataDirectory()
    {
       return Task.FromResult(FileSystem.AppDataDirectory);
    }
}
