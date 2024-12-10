using System.Text.Json;
using BTCPayApp.Core.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace BTCPayApp.Desktop;

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