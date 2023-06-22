namespace Comrade.Core.Contracts;

public interface IDataDirectoryProvider
{
    Task<string> GetAppDataDirectory();
}