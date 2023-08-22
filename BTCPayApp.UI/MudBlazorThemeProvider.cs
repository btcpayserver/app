using BTCPayApp.Core.Contracts;
using MudBlazor;

namespace BTCPayApp.UI;

public class MudBlazorThemeProvider : ISystemThemeProvider
{
    private MudThemeProvider? _mudThemeProvider;

    public MudThemeProvider? MudThemeProvider
    {
        get => _mudThemeProvider;
        set
        {
            _mudThemeProvider = value;
            if (_mudThemeProvider is not null)
            {
                _mudThemeProvider.WatchSystemPreference(b =>
                {
                    SystemThemeChanged?.Invoke(this, b);
                    return Task.CompletedTask;
                });
            }
        }
    }

    public Task<bool> IsDarkMode()
    {
        return Task.FromResult(MudThemeProvider?.IsDarkMode is true);
    }

    public event EventHandler<bool>? SystemThemeChanged;
}