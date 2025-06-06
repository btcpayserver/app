using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.Models;
using BTCPayServer.Client.Models;

namespace BTCPayApp.Core.Auth;

public interface IAccountManager
{
    public BTCPayAccount? Account { get; }
    public AppUserInfo? UserInfo { get; }
    public AppUserStoreInfo? CurrentStore { get; }
    public BTCPayAppClient GetClient(string? baseUri = null, string? token = null);
    public Task<string?> GetEncryptionKey();
    public Task SetEncryptionKey(string value);
    public Task<bool> CheckAuthenticated(bool refreshUser = false);
    public Task<bool> IsAuthorized(string policy, object? resource = null);
    public Task<FormResult> AddAccountWithEncyptionKey(string serverUrl, string email, string key);
    public Task<FormResult<AcceptInviteResult>> AcceptInvite(string inviteUrl, CancellationToken? cancellation = default);
    public Task<FormResult<LoginInfoResult>> LoginInfo(string serverUrl, string email, CancellationToken? cancellation = default);
    public Task<FormResult> Login(string serverUrl, string email, string password, string? otp, CancellationToken? cancellation = default);
    public Task<FormResult> LoginWithCode(string serverUrl, string? email, string code, CancellationToken? cancellation = default);
    public Task<FormResult> Register(string serverUrl, string email, string password, CancellationToken? cancellation = default);
    public Task<FormResult> ResetPassword(string serverUrl, string email, string? resetCode, string? newPassword, CancellationToken? cancellation = default);
    public Task<FormResult<ApplicationUserData>> ChangePassword(string currentPassword, string newPassword, CancellationToken? cancellation = default);
    public Task<FormResult> SwitchMode(string storeId, string mode, CancellationToken? cancellation = default);
    public Task<FormResult> SwitchToOwner(string password, string? otp, CancellationToken? cancellation = default);
    public Task<FormResult> SetCurrentStoreId(string? storeId);
    public Task<AppUserStoreInfo> EnsureStorePos(AppUserStoreInfo store, bool? forceCreate = false);
    public Task Logout();
    public AsyncEventHandler<AppUserInfo?>? OnUserInfoChanged { get; set; }
    public AsyncEventHandler<AppUserStoreInfo?>? OnStoreChanged { get; set; }
    public AsyncEventHandler<string>? OnEncryptionKeyChanged { get; set; }
}
