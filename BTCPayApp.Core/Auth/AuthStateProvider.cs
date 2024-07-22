using System.Security.Claims;
using BTCPayApp.CommonServer.Models;
using BTCPayApp.Core.AspNetRip;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BTCPayApp.Core.Auth;

public class AuthStateProvider : AuthenticationStateProvider, IAccountManager, IHostedService
{
    private const string AccountKeyPrefix = "Account";
    private const string CurrentAccountKey = "CurrentAccount";
    private bool _isInitialized;
    private BTCPayAccount? _account;

    // TODO: Move _userInfo to state
    private AppUserInfo? _userInfo;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ClaimsPrincipal _unauthenticated = new(new ClaimsIdentity());
    private readonly IOptionsMonitor<IdentityOptions> _identityOptions;
    private readonly IAuthorizationService _authService;
    private readonly IConfigProvider _config;

    public BTCPayAccount? GetAccount() => _account;
    public AppUserInfo? GetUserInfo() => _userInfo;

    public AsyncEventHandler<BTCPayAccount?>? OnBeforeAccountChange { get; set; }
    public AsyncEventHandler<BTCPayAccount?>? OnAfterAccountChange { get; set; }

    public AuthStateProvider(
        IConfigProvider config,
        IAuthorizationService authService,
        IOptionsMonitor<IdentityOptions> identityOptions)
    {
        _config = config;
        _authService = authService;
        _identityOptions = identityOptions;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = PingOccasionally();
    }

    private async Task PingOccasionally()
    {
        while (_userInfo != null)
        {
            await GetAuthenticationStateAsync();
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public BTCPayAppClient GetClient(string? baseUri = null)
    {
        if (string.IsNullOrEmpty(baseUri) && string.IsNullOrEmpty(_account?.BaseUri))
            throw new ArgumentException("No base URI present or provided.", nameof(baseUri));
        var client = new BTCPayAppClient(baseUri ?? _account!.BaseUri);
        if (string.IsNullOrEmpty(baseUri) && !string.IsNullOrEmpty(_account?.AccessToken) && !string.IsNullOrEmpty(_account.RefreshToken))
            client.SetAccess(_account.AccessToken, _account.RefreshToken, _account.AccessExpiry.GetValueOrDefault());
        client.AccessRefreshed += OnAccessRefresh;
        return client;
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
            if (_userInfo == null && _account?.HasTokens is true)
            {
                var cts = new CancellationTokenSource(5000);
                await FetchUserInfo(cts.Token);
            }

            if (_userInfo != null)
            {
                var claims = new List<Claim>
                {
                    new(_identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, _userInfo.UserId!),
                    new(_identityOptions.CurrentValue.ClaimsIdentity.UserNameClaimType, _userInfo.Name ?? _userInfo.Email!),
                    new(_identityOptions.CurrentValue.ClaimsIdentity.EmailClaimType, _userInfo.Email!)
                };
                if (_userInfo.Roles?.Any() is true)
                    claims.AddRange(_userInfo.Roles.Select(role =>
                        new Claim(_identityOptions.CurrentValue.ClaimsIdentity.RoleClaimType, role)));
                if (_userInfo.Stores?.Any() is true)
                    claims.AddRange(_userInfo.Stores.Select(store =>
                        new Claim(store.Id, string.Join(',', store.Permissions))));
                user = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSchemes.GreenfieldBearer));

                // update account user info
                _account!.SetInfo(_userInfo.Email!, _userInfo.Name, _userInfo.ImageUrl);
                await UpdateAccount(_account);
            }

            var res = new AuthenticationState(user);
            if (AppUserInfo.Equals(oldUserInfo, _userInfo))
                return res;

            NotifyAuthenticationStateChanged(Task.FromResult(res));
            return res;
        }
        catch
        {
            return new AuthenticationState(user);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> CheckAuthenticated(bool refreshUser = false)
    {
        if (refreshUser) await FetchUserInfo();
        await GetAuthenticationStateAsync();
        return _userInfo != null;
    }

    public async Task<bool> IsAuthorized(string policy, object? resource = null)
    {
        var authState = await GetAuthenticationStateAsync();
        var result = await _authService.AuthorizeAsync(authState.User, resource,Policies.CanViewStoreSettings);
        return result.Succeeded;
    }

    public async Task Logout()
    {
        _userInfo = null;
        _account!.ClearAccess();
        await UpdateAccount(_account);
        await SetCurrentAccount(null);
    }

    public async Task<FormResult> SetCurrentStoreId(string storeId)
    {
        var store = GetUserStore(storeId);
        if (store == null) return new FormResult(false, $"Store with ID '{storeId}' does not exist or belong to the user.");

        string? message = null;

        // create associated POS app if there is none
        if (string.IsNullOrEmpty(store.PosAppId))
        {
            try
            {
                var posConfig = new PointOfSaleAppRequest { AppName = store.Name, DefaultView = PosViewType.Light };
                var app = await GetClient().CreatePointOfSaleApp(store.Id, posConfig);
                message = $"The Point of Sale called \"{app.AppName}\" has been created for use with the app.";

                await FetchUserInfo();
            }
            catch (Exception e)
            {
                return new FormResult(false, e.Message);
            }
        }

        _account!.CurrentStoreId = storeId;
        await UpdateAccount(_account);

        return new FormResult(true, string.IsNullOrEmpty(message) ? null : [message]);
    }

    public async Task UnsetCurrentStore()
    {
        _account!.CurrentStoreId = null;
        await UpdateAccount(_account);
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
            var account = await GetAccount(serverUrl, response.Email);
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
            var expiryOffset = DateTimeOffset.Now;
            var response = await GetClient(serverUrl).Login(payload, cancellation.GetValueOrDefault());
            var account = await GetAccount(serverUrl, email);
            account.SetAccess(response.AccessToken, response.RefreshToken, response.ExpiresIn, expiryOffset);
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
            var expiryOffset = DateTimeOffset.Now;
            var client = GetClient(serverUrl);
            var response = await client.Login(code, cancellation.GetValueOrDefault());
            var account = await GetAccount(serverUrl, email);
            account.SetAccess(response.AccessToken, response.RefreshToken, response.ExpiresIn, expiryOffset);
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
        var payload = new SignupRequest
        {
            Email = email,
            Password = password
        };
        try
        {
            var expiryOffset = DateTimeOffset.Now;
            var response = await GetClient(serverUrl).RegisterUser(payload, cancellation.GetValueOrDefault());
            var account = new BTCPayAccount(serverUrl, email);
            var message = "Account created.";
            if (response.ContainsKey("accessToken"))
            {
                var access = response.ToObject<AccessTokenResponse>();
                account.SetAccess(access.AccessToken, access.RefreshToken, access.ExpiresIn, expiryOffset);
            }
            else
            {
                var signup = response.ToObject<SignupResult>();
                if (signup.RequiresConfirmedEmail)
                    message += " Please confirm your email.";
                if (signup.RequiresUserApproval)
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
            await GetClient(serverUrl).ResetPassword(payload, cancellation.GetValueOrDefault());
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
            _userInfo!.SetInfo(userData.Email!, userData.Name, userData.ImageUrl);
            return new FormResult<ApplicationUserData>(true, "Your account info has been changed.", userData);
        }
        catch (Exception e)
        {
            return new FormResult<ApplicationUserData>(false, e.Message, null);
        }
    }

    public async Task<FormResult> RefreshAccess(CancellationToken? cancellation = default)
    {
        try
        {
            await GetClient().RefreshAccess(_account!.RefreshToken, cancellation);
            return new FormResult(true);
        }
        catch (Exception e)
        {
            return new FormResult(false, e.Message);
        }
    }

    private async void OnAccessRefresh(object? sender, AccessTokenResult access)
    {
        if (_account == null) return;
        _account.SetAccess(access.AccessToken, access.RefreshToken, access.Expiry);
        await UpdateAccount(_account);
    }

    private static string GetKey(string accountId) => $"{AccountKeyPrefix}:{accountId}";

    public async Task<IEnumerable<BTCPayAccount>> GetAccounts(string? hostFilter = null)
    {
        var prefix = $"{AccountKeyPrefix}:" + (hostFilter == null ? "" : $"{hostFilter}:");
        var keys = (await _config.List(prefix)).ToArray();
        var accounts = new List<BTCPayAccount>();
        foreach (var key in keys)
        {
            var account = await _config.Get<BTCPayAccount>(key);
            accounts.Add(account!);
        }
        return accounts;
    }

    public async Task UpdateAccount(BTCPayAccount account)
    {
        await _config.Set(GetKey(account.Id), account);
    }

    public async Task RemoveAccount(BTCPayAccount account)
    {
        await _config.Set<BTCPayAccount>(GetKey(account.Id), null);
    }

    private async Task<BTCPayAccount> GetAccount(string serverUrl, string email)
    {
        var accountId = BTCPayAccount.GetId(serverUrl, email);
        var account = await _config.Get<BTCPayAccount>(GetKey(accountId));
        return account ?? new BTCPayAccount(serverUrl, email);
    }

    private async Task<BTCPayAccount?> GetCurrentAccount()
    {
        var accountId = await _config.Get<string>(CurrentAccountKey);
        if (string.IsNullOrEmpty(accountId)) return null;
        return await _config.Get<BTCPayAccount>(GetKey(accountId));
    }

    private async Task SetCurrentAccount(BTCPayAccount? account)
    {
        OnBeforeAccountChange?.Invoke(this, _account);
        if (account != null) await UpdateAccount(account);
        await _config.Set(CurrentAccountKey, account?.Id);
        _account = account;
        _userInfo = null;

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        OnAfterAccountChange?.Invoke(this, _account);
    }

    private async Task FetchUserInfo(CancellationToken cancellationToken = default)
    {
        _userInfo = await GetClient().GetUserInfo(cancellationToken);
    }
}
