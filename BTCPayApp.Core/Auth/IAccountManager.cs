using BTCPayApp.CommonServer.Models;

namespace BTCPayApp.Core.Auth;

public interface IAccountManager
{
    public BTCPayAccount? GetAccount();
    public AppUserInfo? GetUserInfo();
    public Task<AppUserInfo?> FetchUserInfo();
    public Task<bool> CheckAuthenticated(bool refreshUser = false);
    public Task<FormResult> Login(string serverUrl, string email, string password, string? otp, CancellationToken? cancellation = default);
    public Task<FormResult> Register(string serverUrl, string email, string password, CancellationToken? cancellation = default);
    public Task<FormResult> ResetPassword(string serverUrl, string email, string? resetCode, string? newPassword, CancellationToken? cancellation = default);
    public Task SetCurrentStoreId(string storeId);
    public AppUserStoreInfo? GetCurrentStore();
    public AppUserStoreInfo? GetUserStore(string storeId);
    public Task Logout();

    // general request methods
    public Task<TResponse> Get<TResponse>(string path, CancellationToken cancellation = default);
    public Task Post<TRequest>(string path, TRequest payload, CancellationToken cancellation = default);
    public Task<TResponse?> Post<TRequest, TResponse>(string path, TRequest payload, CancellationToken cancellation = default);
    public Task Put<TRequest>(string path, TRequest payload, CancellationToken cancellation = default);
    public Task<TResponse?> Put<TRequest, TResponse>(string path, TRequest payload, CancellationToken cancellation = default);
    public Task Delete<TRequest>(string path, TRequest payload, CancellationToken cancellation = default);
    public Task<TResponse?> Delete<TRequest, TResponse>(string path, TRequest payload, CancellationToken cancellation = default);
}
