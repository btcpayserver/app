using System.Security.Claims;
using BTCPayApp.CommonServer;
using BTCPayApp.Core;
using BTCPayApp.Core.Contracts;
using BTCPayApp.UI.Models;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.Extensions.Options;

namespace BTCPayApp.UI.Auth;

public class AuthStateProvider(
    BTCPayAppClient client,
    ISecureConfigProvider config,
    IOptionsMonitor<IdentityOptions> identityOptions
) : AuthenticationStateProvider, IAccountManager
{
    private bool _isInitialized;
    private BTCPayAccount? _account;
    private AppUserInfo? _userInfo;
    private readonly ClaimsPrincipal _unauthenticated = new(new ClaimsIdentity());

    public BTCPayAccount? GetAccount() => _account;
    public AppUserInfo? GetUserInfo() => _userInfo;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // default to unauthenticated
        var user = _unauthenticated;

        // initialize with persisted account
        if (!_isInitialized && _account == null)
        {
            _account = await config.Get<BTCPayAccount>("account");
            if (!string.IsNullOrEmpty(_account?.AccessToken) && !string.IsNullOrEmpty(_account.RefreshToken))
                client.SetAccess(_account.AccessToken, _account.RefreshToken, _account.AccessExpiry.GetValueOrDefault());
            else
                client.ClearAccess();
            _isInitialized = true;
        }

        if (_account != null && _userInfo == null)
        {
            try
            {
                _userInfo = await client.Get<AppUserInfo>(_account!.BaseUri, "info");
            }
            catch { /* ignored */ }
        }

        if (_userInfo != null)
        {
            var claims = new List<Claim>
            {
                new (identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, _userInfo.UserId),
                new (identityOptions.CurrentValue.ClaimsIdentity.UserNameClaimType, _userInfo.Email),
                new (identityOptions.CurrentValue.ClaimsIdentity.EmailClaimType, _userInfo.Email)
            };
            claims.AddRange(_userInfo.Roles.Select(role =>
                new Claim(identityOptions.CurrentValue.ClaimsIdentity.RoleClaimType, role)));
            user = new ClaimsPrincipal(new ClaimsIdentity(claims, nameof(AuthStateProvider)));
        }

        return new AuthenticationState(user);
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
        await config.Set("account", _account);
        client.ClearAccess();

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private async Task SetAccount(BTCPayAccount account)
    {
        _account = account;
        await config.Set("account", _account);
        client.SetAccess(_account.AccessToken!, _account.RefreshToken!, _account.AccessExpiry.GetValueOrDefault());

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
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
            var response = await client.Post<LoginRequest, AccessTokenResponse>(serverUrl, "login", payload, cancellation.GetValueOrDefault());
            var account = new BTCPayAccount(serverUrl, email);
            account.SetAccess(response.AccessToken, response.RefreshToken, response.ExpiresIn, expiryOffset);
            await SetAccount(account);
            return new FormResult(true);
        }
        catch (BTCPayAppClientException e)
        {
            return new FormResult(e.Message);
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
            var path = string.IsNullOrEmpty(payload.ResetCode) && string.IsNullOrEmpty(payload.NewPassword)
                ? "forgot-password"
                : "reset-password";
            await client.Post(serverUrl, path, payload, cancellation.GetValueOrDefault());
            return new FormResult(true);
        }
        catch (BTCPayAppClientException e)
        {
            return new FormResult(e.Message);
        }
    }
}
