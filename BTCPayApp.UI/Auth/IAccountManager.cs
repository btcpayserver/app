using BTCPayApp.CommonServer;
using BTCPayApp.Core;
using BTCPayApp.UI.Models;

namespace BTCPayApp.UI.Auth;

public interface IAccountManager
{
    public BTCPayAccount? GetAccount();
    public AppUserInfo? GetUserInfo();
    public Task<bool> CheckAuthenticated();
    public Task<FormResult> Login(string serverUrl, string email, string password, string? otp, CancellationToken? cancellation = default);
    public Task<FormResult> Register(string serverUrl, string email, string password, CancellationToken? cancellation = default);
    public Task<FormResult> ResetPassword(string serverUrl, string email, string? resetCode, string? newPassword, CancellationToken? cancellation = default);
    public Task Logout();
}
