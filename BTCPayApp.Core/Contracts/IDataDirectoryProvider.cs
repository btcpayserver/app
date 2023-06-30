namespace BTCPayApp.Core.Contracts;

public interface IDataDirectoryProvider
{
    Task<string> GetAppDataDirectory();
}