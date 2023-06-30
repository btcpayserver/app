#nullable enable
using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Maui.Services;

public class XamarinDataDirectoryProvider: IDataDirectoryProvider
{
    public Task<string> GetAppDataDirectory()
    {
       return Task.FromResult(FileSystem.AppDataDirectory);
    }
}