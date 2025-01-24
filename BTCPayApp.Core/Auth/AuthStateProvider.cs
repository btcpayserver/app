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
    IAuthorizationService authService,
    ConfigProvider configProvider,
    IOptionsMonitor<IdentityOptions> identityOptions)
    : AuthenticationStateProvider, IAccountManager, IHostedService
{
    private bool _isInitialized;
    private bool _refreshUserInfo;
    private CancellationTokenSource? _pingCts;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ClaimsPrincipal _unauthenticated = new(new ClaimsIdentity());

    public BTCPayAccount? Account { get; private set; }
    public AppUserInfo? UserInfo { get; private set; }
    public string? CurrentStoreId { get; private set; }
    public AsyncEventHandler<BTCPayAccount?>? OnBeforeAccountChange { get; set; }
    public AsyncEventHandler<BTCPayAccount?>? OnAfterAccountChange { get; set; }
    public AsyncEventHandler<AppUserStoreInfo?>? OnBeforeStoreChange { get; set; }
    public AsyncEventHandler<AppUserStoreInfo?>? OnAfterStoreChange { get; set; }
    public AsyncEventHandler<AppUserInfo?>? OnUserInfoChange { get; set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pingCts = new CancellationTokenSource();
        //syncService.LocalUpdated += SyncServiceLocalUpdated;
        _ = PingOccasionally(_pingCts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        //syncService.LocalUpdated -= SyncServiceLocalUpdated;
        _pingCts?.Cancel();
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

    /*private Task SyncServiceLocalUpdated(object? sender, string[] keys)
    {
        if (keys.Contains(BTCPayAppConfig.Key))
        {
            // TODO: Implement this method
        }
        return Task.CompletedTask;
    }*/

    public BTCPayAppClient GetClient(string? baseUri = null)
    {
        if (string.IsNullOrEmpty(baseUri) && string.IsNullOrEmpty(Account?.BaseUri))
            throw new ArgumentException("No base URI present or provided.", nameof(baseUri));
        return new BTCPayAppClient(baseUri ?? Account!.BaseUri, Account?.AccessToken, clientFactory.CreateClient());
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // default to unauthenticated
        var user = _unauthenticated;
        try
        {
            await _semaphore.WaitAsync();

            // initialize with persisted account
            if (!_isInitialized && Account == null)
            {
                Account = await configProvider.Get<BTCPayAccount>(BTCPayAccount.Key);
                CurrentStoreId = (await configProvider.Get<BTCPayAppConfig>(BTCPayAppConfig.Key))?.CurrentStoreId;
                _isInitialized = true;
            }

            var oldUserInfo = UserInfo;
            var needsRefresh = _refreshUserInfo || UserInfo == null;
            if (needsRefresh && !string.IsNullOrEmpty(Account?.AccessToken))
            {
                var cts = new CancellationTokenSource(5000);
                UserInfo = await GetClient().GetUserInfo(cts.Token);
                _refreshUserInfo = false;
            }

            if (Account != null && UserInfo != null)
            {
                var claims = new List<Claim>
                {
                    new(identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, UserInfo.UserId!),
                    new(identityOptions.CurrentValue.ClaimsIdentity.UserNameClaimType, UserInfo.Name ?? UserInfo.Email!),
                    new(identityOptions.CurrentValue.ClaimsIdentity.EmailClaimType, UserInfo.Email!)
                };
                if (UserInfo.Roles?.Any() is true)
                    claims.AddRange(UserInfo.Roles.Select(role =>
                        new Claim(identityOptions.CurrentValue.ClaimsIdentity.RoleClaimType, role)));
                if (UserInfo.Stores?.Any() is true)
                    claims.AddRange(UserInfo.Stores.Select(store =>
                        new Claim(store.Id, string.Join(',', store.Permissions ?? []))));
                user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Greenfield"));
            }

            var res = new AuthenticationState(user);
            if (AppUserInfo.Equals(oldUserInfo, UserInfo)) return res;

            if (Account != null && UserInfo != null)
            {
                OnUserInfoChange?.Invoke(this, UserInfo);
                await UpdateAccount(Account);
            }

            NotifyAuthenticationStateChanged(Task.FromResult(res));
            return res;
        }
        catch
        {
            UserInfo = null;
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
        return UserInfo != null;
    }

    public async Task<bool> IsAuthorized(string policy, object? resource = null)
    {
        var authState = await GetAuthenticationStateAsync();
        var result = await authService.AuthorizeAsync(authState.User, resource, policy);
        return result.Succeeded;
    }

    public async Task<FormResult> SetCurrentStoreId(string? storeId)
    {
        if (!string.IsNullOrEmpty(storeId))
        {
            var store = GetUserStore(storeId);
            if (store == null) return new FormResult(false, $"Store with ID '{storeId}' does not exist or belong to the user.");

            if (store.Id != GetCurrentStore()?.Id)
                await SetCurrentStore(store);
        }
        else
        {
            await SetCurrentStore(null);
        }
        return new FormResult(true);
    }

    private async Task SetCurrentStore(AppUserStoreInfo? store)
    {
        OnBeforeStoreChange?.Invoke(this, GetCurrentStore());

        if (store != null)
            store = await EnsureStorePos(store);

        CurrentStoreId = store?.Id;

        var appConfig = await configProvider.Get<BTCPayAppConfig>(BTCPayAppConfig.Key) ?? new BTCPayAppConfig();
        appConfig.CurrentStoreId = CurrentStoreId;
        await configProvider.Set(BTCPayAppConfig.Key, appConfig, true);

        OnAfterStoreChange?.Invoke(this, store);
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

    private AppUserStoreInfo? GetUserStore(string storeId)
    {
        return UserInfo?.Stores?.FirstOrDefault(store => store.Id == storeId);
    }

    public AppUserStoreInfo? GetCurrentStore()
    {
        return string.IsNullOrEmpty(CurrentStoreId) ? null : GetUserStore(CurrentStoreId);
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
            var account = new BTCPayAccount(serverUrl, response.Email!);
            await SetAccount(account);
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
            var account = new BTCPayAccount(serverUrl, email, response.AccessToken);
            await SetAccount(account);
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
            var account = new BTCPayAccount(serverUrl, email, response.AccessToken);
            await SetAccount(account);
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
            var account = new BTCPayAccount(serverUrl, email);
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
            await SetAccount(account);
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
                var account = new BTCPayAccount(serverUrl, email, access!.AccessToken);
                await SetAccount(account);
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
            if (UserInfo != null)
            {
                UserInfo.SetInfo(userData.Email!, userData.Name, userData.ImageUrl);
                OnUserInfoChange?.Invoke(this, UserInfo);
            }
            return new FormResult<ApplicationUserData>(true, "Your account info has been changed.", userData);
        }
        catch (Exception e)
        {
            return new FormResult<ApplicationUserData>(false, e.Message, null);
        }
    }

    public async Task Logout()
    {
        if (Account == null) return;
        Account.AccessToken = null;
        await SetAccount(Account);
    }

    private async Task UpdateAccount(BTCPayAccount account)
    {
        await configProvider.Set(BTCPayAccount.Key, account, false);
    }

    private async Task SetAccount(BTCPayAccount account)
    {
        OnBeforeAccountChange?.Invoke(this, Account);
        await UpdateAccount(account);
        Account = account;
        UserInfo = null;
        OnUserInfoChange?.Invoke(this, UserInfo);

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        OnAfterAccountChange?.Invoke(this, Account);

        var store = GetCurrentStore();
        if (store != null) await SetCurrentStore(store);
    }
}
