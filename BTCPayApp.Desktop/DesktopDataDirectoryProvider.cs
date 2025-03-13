using BTCPayApp.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Desktop;

public class DesktopDataDirectoryProvider : IDataDirectoryProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DesktopDataDirectoryProvider> _logger;

    public DesktopDataDirectoryProvider(IConfiguration configuration, ILogger<DesktopDataDirectoryProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private string? _dataDir;
    private string? _cacheDir;

    public virtual Task<string> GetAppDataDirectory()
    {
        if (_dataDir != null)
            return Task.FromResult(_dataDir);
        var def = "BTCPayApp";
        var dirName = _configuration.GetValue("BTCPAYAPP_DIRNAME", def);
        _dataDir = GetDirectory(dirName?? def);
        _logger.LogInformation($"Using data directory: {_dataDir}");
        return Task.FromResult(_dataDir);
    }

    public virtual Task<string> GetCacheDirectory()
    {
        if (_cacheDir != null)
            return Task.FromResult(_cacheDir);
        var def = "BTCPayApp";
        var dirName = _configuration.GetValue("BTCPAYAPP_DIRNAME", def);
        _cacheDir = GetDirectory(Path.Combine(dirName ?? def, "cache"));
        _logger.LogInformation($"Using cache directory: {_cacheDir}");
        return Task.FromResult(_cacheDir);
    }

    private string GetDirectory(string appDirectory)
    {
        var environmentVariable1 = _configuration.GetValue<string>("HOME");
        var environmentVariable2 = _configuration.GetValue<string>("APPDATA");
        string str;
        if (!string.IsNullOrEmpty(environmentVariable1) && string.IsNullOrEmpty(environmentVariable2))
            str = Path.Combine(environmentVariable1, "." + appDirectory.ToLowerInvariant());
        else if (!string.IsNullOrEmpty(environmentVariable2))
        {
            str = Path.Combine(environmentVariable2, appDirectory);
        }
        else
        {
            throw new DirectoryNotFoundException(
                "Could not find suitable datadir environment variables HOME or APPDATA are not set");
        }

        if (!Directory.Exists(str))
            Directory.CreateDirectory(str);
        return str;
    }
}
