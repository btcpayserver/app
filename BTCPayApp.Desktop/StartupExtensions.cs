using System.Text.Json;
using BTCPayApp.Core.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayApp.Desktop;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureBTCPayAppDesktop(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddDataProtection();
        serviceCollection.AddSingleton<IDataDirectoryProvider, DesktopDataDirectoryProvider>();
        serviceCollection.AddSingleton<IConfigProvider, DesktopConfigProvider>();
        serviceCollection.AddSingleton<ISecureConfigProvider, DesktopSecureConfigProvider>();
        return serviceCollection;
    }
}

public class DesktopSecureConfigProvider: DesktopConfigProvider, ISecureConfigProvider
{
    private readonly IDataProtector _dataProtector;

    public DesktopSecureConfigProvider(IDataDirectoryProvider directoryProvider, IDataProtectionProvider dataProtectionProvider) : base(directoryProvider)
    {
        _dataProtector = dataProtectionProvider.CreateProtector("SecureConfig");
    }

    protected override Task<string> ReadFromRaw(string str) => Task.FromResult(_dataProtector.Unprotect(str));
    protected override Task<string> WriteFromRaw(string str) => Task.FromResult(_dataProtector.Protect(str));
}

public class DesktopConfigProvider : IConfigProvider
{
    private readonly Task<string> _configDir;

    public DesktopConfigProvider(IDataDirectoryProvider directoryProvider)
    {
        _configDir = directoryProvider.GetAppDataDirectory().ContinueWith(task =>
        {
          var res =  Path.Combine(task.Result, "config");
          Directory.CreateDirectory(res);
          return res;
        });
    }

    public async Task<T?> Get<T>(string key)
    {
        var dir = Path.Combine(await _configDir, key);
        if (!File.Exists(dir))
        {
            return default;
        }
        var raw = await File.ReadAllTextAsync(dir);
        return JsonSerializer.Deserialize<T>(await ReadFromRaw(raw));
    }

    protected virtual Task<string> ReadFromRaw(string str) => Task.FromResult(str);
    protected virtual Task<string> WriteFromRaw(string str) => Task.FromResult(str);

    public async Task Set<T>(string key, T? value)
    {
        var dir = Path.Combine(await _configDir, key);
        if (value is null)
        {
            if (File.Exists(dir))
            {
                File.Delete(dir);
            }
        }
        else
        {
            var raw = JsonSerializer.Serialize(value);
            await File.WriteAllTextAsync(dir, await WriteFromRaw(raw));
        }
    }
}

public class DesktopDataDirectoryProvider : IDataDirectoryProvider
{
    private readonly IConfiguration _configuration;

    public DesktopDataDirectoryProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    public virtual Task<string> GetAppDataDirectory()
    {
        var dirName = _configuration.GetValue<string>("BTCPAYAPP_DIRNAME", "BTCPayApp");
        return Task.FromResult(GetDirectory(dirName));
    }

    private string GetDirectory(
        string appDirectory)
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
