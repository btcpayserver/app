﻿#nullable enable
using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Maui.Services;

public class MauiSystemThemeProvider : ISystemThemeProvider
{
    public MauiSystemThemeProvider()
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