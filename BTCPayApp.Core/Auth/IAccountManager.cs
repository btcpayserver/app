using BTCPayApp.CommonServer.Models;

namespace BTCPayApp.Core.Auth;

public interface IAccountManager
{
    public BTCPayAccount? GetAccount();
    public AppUserInfo? GetUserInfo();
    public BTCPayAppClient GetClient(string? baseUri = null);
    public Task<bool> CheckAuthenticated(bool refreshUser = false);
    public Task<FormResult> Login(string serverUrl, string email, string password, string? otp, CancellationToken? cancellation = default);
    public Task<FormResult> Register(string serverUrl, string email, string password, CancellationToken? cancellation = default);
    public Task<FormResult> ResetPassword(string serverUrl, string email, string? resetCode, string? newPassword, CancellationToken? cancellation = default);
    public Task<FormResult> SetCurrentStoreId(string storeId);
    public AppUserStoreInfo? GetCurrentStore();
    public AppUserStoreInfo? GetUserStore(string storeId);
    public Task Logout();
}
