using System.Security.Claims;
using BTCPayApp.CommonServer;
using BTCPayApp.Core.AspNetRip;
using BTCPayApp.Core.Contracts;
using BTCPayServer.Abstractions.Constants;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BTCPayApp.Core.Auth;

public class AuthStateProvider : AuthenticationStateProvider, IAccountManager
{
    private bool _isInitialized;
    private BTCPayAccount? _account;
    private AppUserInfo? _userInfo;
    private readonly ClaimsPrincipal _unauthenticated = new(new ClaimsIdentity());
    private readonly IOptionsMonitor<IdentityOptions> _identityOptions;
    private readonly IConfigProvider _config;
    private readonly BTCPayAppClient _client;

    public BTCPayAccount? GetAccount() => _account;
    public AppUserInfo? GetUserInfo() => _userInfo;

    public AuthStateProvider(
        BTCPayAppClient client,
        IConfigProvider config,
        IOptionsMonitor<IdentityOptions> identityOptions)
    {
        _client = client;
        _config = config;
        _identityOptions = identityOptions;

        _client.AccessRefreshed += OnAccessRefresh;
    }

    private SemaphoreSlim _semaphore = new(1, 1);

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            await _semaphore.WaitAsync();

            // default to unauthenticated
            var user = _unauthenticated;

            // initialize with persisted account
            if (!_isInitialized && _account == null)
            {
                _account = await _config.Get<BTCPayAccount>("account");
                if (!string.IsNullOrEmpty(_account?.AccessToken) && !string.IsNullOrEmpty(_account.RefreshToken))
                    _client.SetAccess(_account.AccessToken, _account.RefreshToken,
                        _account.AccessExpiry.GetValueOrDefault());
                else
                    _client.ClearAccess();
                _isInitialized = true;
            }

            var oldUserInfo = _userInfo;
            if (_account != null && _userInfo == null)
            {
                try
                {
                    _userInfo = await _client.Get<AppUserInfo>(_account!.BaseUri, "user");
                }
                catch
                {
                    /* ignored */
                }
            }

            if (_userInfo != null)
            {
                var claims = new List<Claim>
                {
                    new(_identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, _userInfo.UserId!),
                    new(_identityOptions.CurrentValue.ClaimsIdentity.UserNameClaimType, _userInfo.Email!),
                    new(_identityOptions.CurrentValue.ClaimsIdentity.EmailClaimType, _userInfo.Email!)
                };
                if (_userInfo.Roles?.Any() is true)
                    claims.AddRange(_userInfo.Roles.Select(role =>
                        new Claim(_identityOptions.CurrentValue.ClaimsIdentity.RoleClaimType, role)));
                user = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSchemes.Bearer));
            }

            var res = new AuthenticationState(user);
            if (AppUserInfo.Equals(oldUserInfo, _userInfo))
                return res;
            NotifyAuthenticationStateChanged(Task.FromResult(res));
            return res;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> CheckAuthenticated()
    {
        await GetAuthenticationStateAsync();
        return _userInfo != null;
    }

    public async Task Logout()
    {
        _userInfo = null;
        _account!.ClearAccess();
        await _config.Set("account", _account);
        _client.ClearAccess();

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task SetCurrentStoreId(string storeId)
    {
        _account!.CurrentStoreId = storeId;
        await _config.Set("account", _account);
    }

    public AppUserStoreInfo? GetUserStore(string storeId)
    {
        return _userInfo!.Stores?.FirstOrDefault(store => store.Id == storeId);
    }

    public AppUserStoreInfo? GetCurrentStore()
    {
        var storeId = _account?.CurrentStoreId;
        return string.IsNullOrEmpty(storeId) ? null : GetUserStore(storeId);
    }

    private async Task SetAccount(BTCPayAccount account)
    {
        _account = account;
        await _config.Set("account", _account);
        _client.SetAccess(_account.AccessToken!, _account.RefreshToken!, _account.AccessExpiry.GetValueOrDefault());

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private async void OnAccessRefresh(object? sender, AccessTokenResult access)
    {
        if (_account == null) return;
        _account.SetAccess(access.AccessToken, access.RefreshToken, access.Expiry);
        await _config.Set("account", _account);
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
            var response = await _client.Post<LoginRequest, AccessTokenResponse>(serverUrl, "login", payload, cancellation.GetValueOrDefault());
            var account = new BTCPayAccount(serverUrl, email);
            account.SetAccess(response.AccessToken, response.RefreshToken, response.ExpiresIn, expiryOffset);
            await SetAccount(account);
            return new FormResult(true);
        }
        catch (BTCPayAppClientException e)
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
            var response = await _client.Post<SignupRequest, SignupResult>(serverUrl, "register", payload, cancellation.GetValueOrDefault());
            var account = new BTCPayAccount(serverUrl, email);
            await SetAccount(account);
            var message = "Account created.";
            if (response.RequiresConfirmedEmail)
                message += " Please confirm your email.";
            if (response.RequiresUserApproval)
                message += " The new account requires approval by an admin before you can log in.";
            return new FormResult(true, message);
        }
        catch (BTCPayAppClientException e)
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
            var path = isForgotStep ? "forgot-password" : "reset-password";
            await _client.Post(serverUrl, path, payload, cancellation.GetValueOrDefault());
            return new FormResult(true, isForgotStep
                ? "You should have received an email with a password reset code."
                : "Your password has been reset.");
        }
        catch (BTCPayAppClientException e)
        {
            return new FormResult(false, e.Message);
        }
    }
}
