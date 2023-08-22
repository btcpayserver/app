namespace BTCPayApp.Core.Contracts;

public interface ISystemThemeProvider
{
    Task<bool> IsDarkMode();
    
    event EventHandler<bool> SystemThemeChanged; 
}