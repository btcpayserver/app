using System.Text.Json;
using BTCPayApp.Core.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace BTCPayApp.Desktop;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureBTCPayAppDesktop(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddDataProtection(options =>
        {
            options.ApplicationDiscriminator = "BTCPayApp";
        });
        serviceCollection.AddSingleton<IDataDirectoryProvider, DesktopDataDirectoryProvider>();
        // serviceCollection.AddSingleton<IConfigProvider, DesktopConfigProvider>();
        serviceCollection.AddSingleton<ISecureConfigProvider, DesktopSecureConfigProvider>();
        serviceCollection.AddSingleton<IFingerprint, StubFingerprintProvider>();
        return serviceCollection;
    }
}

public class DesktopSecureConfigProvider: ISecureConfigProvider
{
    private readonly IDataProtector _dataProtector;

    public DesktopSecureConfigProvider(IDataDirectoryProvider directoryProvider, IDataProtectionProvider dataProtectionProvider) 
    {
        _dataProtector = dataProtectionProvider.CreateProtector("SecureConfig");
        _configDir = directoryProvider.GetAppDataDirectory().ContinueWith(task =>
        {
            var res =  Path.Combine(task.Result, "config");
            Directory.CreateDirectory(res);
            return res;
        });
    }
    
    private readonly Task<string> _configDir;

    public async Task<T?> Get<T>(string key)
    {
        var dir = Path.Combine(await _configDir, key);
        if (!File.Exists(dir))
        {
            return default;
        }
        var raw = await File.ReadAllTextAsync(dir);
        var json = await ReadFromRaw(raw);
        return JsonSerializer.Deserialize<T>(json);
    }


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

    public async Task<IEnumerable<string>> List(string prefix)
    {
        var dir = await _configDir;
        if (!Directory.Exists(dir))
        {
            return Array.Empty<string>();
        }
        return Directory.GetFiles(dir, $"{prefix}*").Select(Path.GetFileName).Where(p => p?.StartsWith(prefix) is true)!;
    }

    protected Task<string> ReadFromRaw(string str) => Task.FromResult(_dataProtector.Unprotect(str));
    protected Task<string> WriteFromRaw(string str) => Task.FromResult(_dataProtector.Protect(str));
}

public class StubFingerprintProvider: IFingerprint
{
    public Task<FingerprintAvailability> GetAvailabilityAsync(bool allowAlternativeAuthentication = false)
    {
        return Task.FromResult(FingerprintAvailability.NoImplementation);
    }

    public Task<bool> IsAvailableAsync(bool allowAlternativeAuthentication = false)
    {
        return Task.FromResult(false);
    }

    public Task<FingerprintAuthenticationResult> AuthenticateAsync(AuthenticationRequestConfiguration authRequestConfig,
        CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public Task<AuthenticationType> GetAuthenticationTypeAsync()
    {
        throw new NotImplementedException();
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
