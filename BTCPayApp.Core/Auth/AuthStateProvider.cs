using System.Security.Claims;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.Models;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BTCPayApp.Core.Auth;

public class AuthStateProvider(
    IHttpClientFactory clientFactory,
    ConfigProvider configProvider,
    IAuthorizationService authService,
    IOptionsMonitor<IdentityOptions> identityOptions)
    : AuthenticationStateProvider, IAccountManager, IHostedService
{
    private const string AccountKeyPrefix = "Account";
    private const string CurrentAccountKey = "CurrentAccount";
    private bool _isInitialized;
    private bool _refreshUserInfo;
    private BTCPayAccount? _account;
    private AppUserInfo? _userInfo;
    private CancellationTokenSource? _pingCts;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ClaimsPrincipal _unauthenticated = new(new ClaimsIdentity());

    public BTCPayAccount? GetAccount() => _account;
    public AppUserInfo? GetUserInfo() => _userInfo;

    public AsyncEventHandler<BTCPayAccount?>? OnBeforeAccountChange { get; set; }
    public AsyncEventHandler<BTCPayAccount?>? OnAfterAccountChange { get; set; }
    public AsyncEventHandler<AppUserStoreInfo?>? OnBeforeStoreChange { get; set; }
    public AsyncEventHandler<AppUserStoreInfo?>? OnAfterStoreChange { get; set; }
    public AsyncEventHandler<BTCPayAccount?>? OnAccountInfoChange { get; set; }
    public AsyncEventHandler<AppUserInfo?>? OnUserInfoChange { get; set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pingCts = new CancellationTokenSource();
        _ = PingOccasionally(_pingCts.Token);
        return Task.CompletedTask;
    }

    private async Task PingOccasionally(CancellationToken pingCtsToken)
    {
        while (pingCtsToken.IsCancellationRequested is false)
        {
            await GetAuthenticationStateAsync();
            await Task.Delay(TimeSpan.FromSeconds(5), pingCtsToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _pingCts?.Cancel();
        return Task.CompletedTask;
    }

    public BTCPayAppClient GetClient(string? baseUri = null)
    {
        if (string.IsNullOrEmpty(baseUri) && string.IsNullOrEmpty(_account?.BaseUri))
            throw new ArgumentException("No base URI present or provided.", nameof(baseUri));
        return new BTCPayAppClient(baseUri ?? _account!.BaseUri, _account?.AccessToken, clientFactory.CreateClient());
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // default to unauthenticated
        var user = _unauthenticated;
        try
        {
            await _semaphore.WaitAsync();

            // initialize with persisted account
            if (!_isInitialized && _account == null)
            {
                _account = await GetCurrentAccount();
                _isInitialized = true;
            }

            var oldUserInfo = _userInfo;
            var needsRefresh = _refreshUserInfo || _userInfo == null;
            if (needsRefresh && !string.IsNullOrEmpty(_account?.AccessToken))
            {
                var cts = new CancellationTokenSource(5000);
                _userInfo = await GetClient().GetUserInfo(cts.Token);
                _refreshUserInfo = false;
            }

            if (_userInfo != null)
            {
                var claims = new List<Claim>
                {
                    new(identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, _userInfo.UserId!),
                    new(identityOptions.CurrentValue.ClaimsIdentity.UserNameClaimType, _userInfo.Name ?? _userInfo.Email!),
                    new(identityOptions.CurrentValue.ClaimsIdentity.EmailClaimType, _userInfo.Email!)
                };
                if (_userInfo.Roles?.Any() is true)
                    claims.AddRange(_userInfo.Roles.Select(role =>
                        new Claim(identityOptions.CurrentValue.ClaimsIdentity.RoleClaimType, role)));
                if (_userInfo.Stores?.Any() is true)
                    claims.AddRange(_userInfo.Stores.Select(store =>
                        new Claim(store.Id, string.Join(',', store.Permissions ?? []))));
                user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Greenfield"));
            }

            var res = new AuthenticationState(user);
            if (AppUserInfo.Equals(oldUserInfo, _userInfo)) return res;

            //TODO: should this check against old user info?s
            if (_userInfo != null)
            {
                OnUserInfoChange?.Invoke(this, _userInfo);
                // update account user info
                _account!.SetInfo(_userInfo.Email!, _userInfo.Name, _userInfo.ImageUrl);
                OnAccountInfoChange?.Invoke(this, _account);
                await UpdateAccount(_account);
            }

            NotifyAuthenticationStateChanged(Task.FromResult(res));
            return res;
        }
        catch
        {
            _userInfo = null;
            return new AuthenticationState(user);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> CheckAuthenticated(bool refreshUser = false)
    {
        if (refreshUser) _refreshUserInfo = true;
        await GetAuthenticationStateAsync();
        return _userInfo != null;
    }

    public async Task<bool> IsAuthorized(string policy, object? resource = null)
    {
        var authState = await GetAuthenticationStateAsync();
        var result = await authService.AuthorizeAsync(authState.User, resource, policy);
        return result.Succeeded;
    }

    public async Task Logout()
    {
        _userInfo = null;
        _account!.AccessToken = null;
        OnUserInfoChange?.Invoke(this, _userInfo);
        await UpdateAccount(_account);
        await SetCurrentAccount(null);
    }

    public async Task<FormResult> SetCurrentStoreId(string storeId)
    {
        var store = GetUserStore(storeId);
        if (store == null) return new FormResult(false, $"Store with ID '{storeId}' does not exist or belong to the user.");

        if (store.Id != GetCurrentStore()?.Id)
            await SetCurrentStore(store);

        return new FormResult(true);
    }

    private async Task SetCurrentStore(AppUserStoreInfo store)
    {
        OnBeforeStoreChange?.Invoke(this, GetCurrentStore());

        // create associated POS app if there is none
        store = await EnsureStorePos(store);

        _account!.CurrentStoreId = store.Id;
        await UpdateAccount(_account);

        OnAfterStoreChange?.Invoke(this, store);
    }

    public async Task UnsetCurrentStore()
    {
        OnBeforeStoreChange?.Invoke(this, GetCurrentStore());
        _account!.CurrentStoreId = null;
        await UpdateAccount(_account);
        OnAfterStoreChange?.Invoke(this, null);
    }

    public async Task<AppUserStoreInfo> EnsureStorePos(AppUserStoreInfo store, bool? forceCreate = false)
    {
        if (string.IsNullOrEmpty(store.PosAppId) || forceCreate is true)
        {
            try
            {
                var posConfig = new PointOfSaleAppRequest { AppName = store.Name, DefaultView = PosViewType.Light };
                await GetClient().CreatePointOfSaleApp(store.Id, posConfig);
                await CheckAuthenticated(true);
                store = GetUserStore(store.Id)!;
            }
            catch (Exception)
            {
                // ignored
            }
        }
        return store;
    }

    public AppUserStoreInfo? GetUserStore(string storeId)
    {
        return _userInfo?.Stores?.FirstOrDefault(store => store.Id == storeId);
    }

    public AppUserStoreInfo? GetCurrentStore()
    {
        var storeId = _account?.CurrentStoreId;
        return string.IsNullOrEmpty(storeId) ? null : GetUserStore(storeId);
    }

    public async Task<FormResult<AcceptInviteResult>> AcceptInvite(string inviteUrl, CancellationToken? cancellation = default)
    {
        var urlParts = inviteUrl.Split("/invite/");
        var serverUrl = urlParts.First();
        var pathParts = urlParts.Last().Split("/");
        var payload = new AcceptInviteRequest
        {
            UserId = pathParts[0],
            Code = pathParts[1]
        };
        try
        {
            var response = await GetClient(serverUrl).AcceptInvite(payload, cancellation.GetValueOrDefault());
            var account = await GetAccount(serverUrl, response.Email!);
            await SetCurrentAccount(account);
            var message = "Invitation accepted.";
            if (response.EmailHasBeenConfirmed is true)
                message += " Your email has been confirmed.";
            if (response.RequiresUserApproval is true)
                message += " The new account requires approval by an admin before you can log in.";
            message += string.IsNullOrEmpty(response.PasswordSetCode)
                ? " Your password has been set by the user who invited you."
                : " Please set your password.";
            return new FormResult<AcceptInviteResult>(true, message, response);
        }
        catch (Exception e)
        {
            return new FormResult<AcceptInviteResult>(false, e.Message, null);
        }
    }

    public async Task<FormResult> Login(string serverUrl, string email, string password, string? otp = null, CancellationToken? cancellation = default)
    {
        var payload = new LoginRequest
        {
            Email = email,
            Password = password,
            TwoFactorCode = otp
        };
        try
        {
            var response = await GetClient(serverUrl).Login(payload, cancellation.GetValueOrDefault());
            if (string.IsNullOrEmpty(response.AccessToken)) throw new Exception("Did not obtain valid API token.");
            var account = await GetAccount(serverUrl, email);
            account.AccessToken = response.AccessToken;
            await SetCurrentAccount(account);
            return new FormResult(true);
        }
        catch (Exception e)
        {
            return new FormResult(false, e.Message);
        }
    }

    public async Task<FormResult> LoginWithCode(string serverUrl, string email, string code, CancellationToken? cancellation = default)
    {
        try
        {
            var client = GetClient(serverUrl);
            var response = await client.Login(code, cancellation.GetValueOrDefault());
            if (string.IsNullOrEmpty(response.AccessToken)) throw new Exception("Did not obtain valid API token.");
            var account = await GetAccount(serverUrl, email);
            account.AccessToken = response.AccessToken;
            await SetCurrentAccount(account);
            return new FormResult(true);
        }
        catch (Exception e)
        {
            return new FormResult(false, e.Message);
        }
    }

    public async Task<FormResult> Register(string serverUrl, string email, string password, CancellationToken? cancellation = default)
    {
        var payload = new CreateApplicationUserRequest
        {
            Email = email,
            Password = password
        };
        try
        {
            var response = await GetClient(serverUrl).RegisterUser(payload, cancellation.GetValueOrDefault());
            var account = await GetAccount(serverUrl, email);
            var message = "Account created.";
            if (response.ContainsKey("accessToken"))
            {
                var access = response.ToObject<AuthenticationResponse>();
                if (string.IsNullOrEmpty(access?.AccessToken)) throw new Exception("Did not obtain valid API token.");
                account.AccessToken = access.AccessToken;
            }
            else
            {
                var signup = response.ToObject<ApplicationUserData>();
                if (signup?.RequiresEmailConfirmation is true)
                    message += " Please confirm your email.";
                if (signup?.RequiresApproval is true)
                    message += " The new account requires approval by an admin before you can log in.";
            }
            await SetCurrentAccount(account);
            return new FormResult(true, message);
        }
        catch (Exception e)
        {
            return new FormResult(false, e.Message);
        }
    }

    public async Task<FormResult> ResetPassword(string serverUrl, string email, string? resetCode = null, string? newPassword = null, CancellationToken? cancellation = default)
    {
        var payload = new ResetPasswordRequest
        {
            Email = email,
            ResetCode = resetCode ?? string.Empty,
            NewPassword = newPassword ?? string.Empty
        };
        try
        {
            var isForgotStep = string.IsNullOrEmpty(payload.ResetCode) && string.IsNullOrEmpty(payload.NewPassword);
            var response = await GetClient(serverUrl).ResetPassword(payload, cancellation.GetValueOrDefault());
            if (response?.ContainsKey("accessToken") is true)
            {
                var access = response.ToObject<AuthenticationResponse>();
                var account = await GetAccount(serverUrl, email);
                account.AccessToken = access!.AccessToken;
                await SetCurrentAccount(account);
            }

            return new FormResult(true, isForgotStep
                ? "You should have received an email with a password reset code."
                : "Your password has been reset.");
        }
        catch (Exception e)
        {
            return new FormResult(false, e.Message);
        }
    }

    public async Task<FormResult<ApplicationUserData>> ChangePassword(string currentPassword, string newPassword, CancellationToken? cancellation = default)
    {
        var payload = new UpdateApplicationUserRequest
        {
            CurrentPassword = currentPassword,
            NewPassword = newPassword
        };
        try
        {
            var response = await GetClient().UpdateCurrentUser(payload, cancellation.GetValueOrDefault());
            return new FormResult<ApplicationUserData>(true, "Your password has been changed.", response);
        }
        catch (Exception e)
        {
            return new FormResult<ApplicationUserData>(false, e.Message, null);
        }
    }

    public async Task<FormResult<ApplicationUserData>> ChangeAccountInfo(string email, string? name, string? imageUrl, CancellationToken? cancellation = default)
    {
        var payload = new UpdateApplicationUserRequest
        {
            Email = email,
            Name = name,
            ImageUrl = imageUrl
        };
        try
        {
            var userData = await GetClient().UpdateCurrentUser(payload, cancellation.GetValueOrDefault());
            _account!.SetInfo(userData.Email!, userData.Name, userData.ImageUrl);
            OnAccountInfoChange?.Invoke(this, _account);
            if (_userInfo != null)
            {
                _userInfo.SetInfo(userData.Email!, userData.Name, userData.ImageUrl);
                OnUserInfoChange?.Invoke(this, _userInfo);
            }
            return new FormResult<ApplicationUserData>(true, "Your account info has been changed.", userData);
        }
        catch (Exception e)
        {
            return new FormResult<ApplicationUserData>(false, e.Message, null);
        }
    }

    private static string GetKey(string accountId) => $"{AccountKeyPrefix}:{accountId}";

    public async Task<IEnumerable<BTCPayAccount>> GetAccounts(string? hostFilter = null)
    {
        var prefix = $"{AccountKeyPrefix}:" + (hostFilter == null ? "" : $"{hostFilter}:");
        var keys = (await configProvider.List(prefix)).ToArray();
        var accounts = new List<BTCPayAccount>();
        foreach (var key in keys)
        {
            var account = await configProvider.Get<BTCPayAccount>(key);
            accounts.Add(account!);
        }
        return accounts;
    }

    public async Task UpdateAccount(BTCPayAccount account)
    {
        await configProvider.Set(GetKey(account.Id), account, false);
    }

    public async Task RemoveAccount(BTCPayAccount account)
    {
        await configProvider.Set<BTCPayAccount>(GetKey(account.Id), null, false);
    }

    private async Task<BTCPayAccount> GetAccount(string serverUrl, string email)
    {
        var accountId = BTCPayAccount.GetId(serverUrl, email);
        var account = await configProvider.Get<BTCPayAccount>(GetKey(accountId));
        return account ?? new BTCPayAccount(serverUrl, email);
    }

    private async Task<BTCPayAccount?> GetCurrentAccount()
    {
        var accountId = await configProvider.Get<string>(CurrentAccountKey);
        if (string.IsNullOrEmpty(accountId)) return null;
        return await configProvider.Get<BTCPayAccount>(GetKey(accountId));
    }

    private async Task SetCurrentAccount(BTCPayAccount? account)
    {
        OnBeforeAccountChange?.Invoke(this, _account);
        if (account != null) await UpdateAccount(account);
        await configProvider.Set(CurrentAccountKey, account?.Id, false);
        _account = account;
        _userInfo = null;

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        OnAfterAccountChange?.Invoke(this, _account);

        var store = GetCurrentStore();
        if (store != null) await SetCurrentStore(store);
    }
}
