#nullable enable
using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Maui.Services;

public class XamarinSystemThemeProvider : ISystemThemeProvider
{
    public XamarinSystemThemeProvider()
    {
        Application.Current.RequestedThemeChanged += (sender, args) =>
        {
            SystemThemeChanged?.Invoke(this, args.RequestedTheme == AppTheme.Dark);
        };
    }
    
    public Task<bool> IsDarkMode()
    {
        return Task.FromResult(Application.Current?.RequestedTheme == AppTheme.Dark);
    }
    

    public event EventHandler<bool>? SystemThemeChanged;
}