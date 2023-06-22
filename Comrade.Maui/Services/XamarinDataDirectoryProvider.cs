#nullable enable
using Comrade.Core.Contracts;

namespace Comrade.Maui.Services;

public class XamarinDataDirectoryProvider: IDataDirectoryProvider
{
    public Task<string> GetAppDataDirectory()
    {
       return Task.FromResult(FileSystem.AppDataDirectory);
    }
}