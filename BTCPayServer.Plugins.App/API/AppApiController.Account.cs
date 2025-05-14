using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayApp.Core.Models;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Plugins.App.Extensions;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using NicolasDorier.RateLimits;
using LoginRequest = BTCPayApp.Core.Models.LoginRequest;
using PosViewType = BTCPayServer.Plugins.PointOfSale.PosViewType;
using ResetPasswordRequest = BTCPayApp.Core.Models.ResetPasswordRequest;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace BTCPayServer.Plugins.App.API;

public partial class AppApiController
{
    [AllowAnonymous]
    [HttpPost("register")]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> Register(CreateApplicationUserRequest signup)
    {
        greenfieldUsersController.ControllerContext.HttpContext = httpContextAccessor.HttpContext!;
        var res = await greenfieldUsersController.CreateUser(signup);
        var user = await userManager.FindByEmailAsync(signup.Email);
        if (res is not CreatedAtActionResult || user is null) return res;

        SignInResult? signInResult = null;
        var policies = await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
        var requiresApproval = policies.RequiresUserApproval && !user.Approved;
        var requiresConfirmedEmail = policies.RequiresConfirmedEmail && !user.EmailConfirmed;
        if (!requiresConfirmedEmail && !requiresApproval)
        {
            signInResult = await signInManager.PasswordSignInAsync(signup.Email, signup.Password, true, true);
        }

        if (signInResult?.Succeeded is true)
        {
            _logger.LogInformation("User {Email} logged in", user.Email);
            return await UserAuthenticated(user);
        }
        return Ok(new ApplicationUserData
        {
            Email = user.Email,
            RequiresApproval = requiresApproval,
            RequiresEmailConfirmation = requiresConfirmedEmail
        });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> Login(LoginRequest login)
    {
        if (string.IsNullOrEmpty(login.Email))
            ModelState.AddModelError(nameof(login.Email), "Missing email");
        if (string.IsNullOrEmpty(login.Password))
            ModelState.AddModelError(nameof(login.Password), "Missing password");
        if (!ModelState.IsValid)
            return this.CreateValidationError(ModelState);

        // Require the user to pass basic checks (approval, confirmed email, not disabled) before they can log on
        var user = await userManager.FindByEmailAsync(login.Email!);
        if (!UserService.TryCanLogin(user, out var message))
            return this.CreateAPIError(401, "unauthenticated", message);

        var signInResult = await signInManager.PasswordSignInAsync(login.Email!, login.Password!, true, true);
        if (signInResult.RequiresTwoFactor)
        {
            if (!string.IsNullOrEmpty(login.TwoFactorCode))
                signInResult = await signInManager.TwoFactorAuthenticatorSignInAsync(login.TwoFactorCode, true, true);
            else if (!string.IsNullOrEmpty(login.TwoFactorRecoveryCode))
                signInResult = await signInManager.TwoFactorRecoveryCodeSignInAsync(login.TwoFactorRecoveryCode);
        }

        // TODO: Add FIDO and LNURL Auth

        if (signInResult.IsLockedOut)
        {
            _logger.LogWarning("User {Email} tried to log in, but is locked out", user.Email);
        }
        else if (signInResult.Succeeded)
        {
            _logger.LogInformation("User {Email} logged in", user.Email);
            return await UserAuthenticated(user);
        }
        return this.CreateAPIError(401, "unauthenticated", signInResult.ToString());
    }

    [AllowAnonymous]
    [HttpPost("login/code")]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> LoginWithCode([FromBody] string loginCode)
    {
        const string errorMessage = "Invalid login attempt.";
        if (!string.IsNullOrEmpty(loginCode))
        {
            var code = loginCode.Split(';').First();
            var userId = userLoginCodeService.Verify(code);
            var user = userId is null ? null : await userManager.FindByIdAsync(userId);
            if (!UserService.TryCanLogin(user, out var message))
                return this.CreateAPIError(401, "unauthenticated", message);

            await signInManager.SignInAsync(user, false, "LoginCode");

            _logger.LogInformation("User {Email} logged in with a login code", user.Email);
            return await UserAuthenticated(user);
        }
        return this.CreateAPIError(401, "unauthenticated", errorMessage);
    }

    [AllowAnonymous]
    [HttpPost("accept-invite")]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> AcceptInvite(AcceptInviteRequest invite)
    {
        if (string.IsNullOrEmpty(invite.UserId) || string.IsNullOrEmpty(invite.Code)) return NotFound();

        var user = await userManager.FindByInvitationTokenAsync<ApplicationUser>(invite.UserId.Trim(), Uri.UnescapeDataString(invite.Code.Trim()));
        if (user == null) return NotFound();

        var requiresEmailConfirmation = user is { RequiresEmailConfirmation: true, EmailConfirmed: false };
        var requiresUserApproval = user is { RequiresApproval: true, Approved: false };
        bool? emailHasBeenConfirmed = requiresEmailConfirmation ? false : null;
        var requiresSetPassword = !await userManager.HasPasswordAsync(user);
        var passwordSetCode = requiresSetPassword ? await userManager.GeneratePasswordResetTokenAsync(user) : null;

        if (requiresEmailConfirmation)
        {
            var emailConfirmCode = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var result = await userManager.ConfirmEmailAsync(user, emailConfirmCode);
            if (result.Succeeded)
            {
                emailHasBeenConfirmed = true;
                var approvalLink = callbackGenerator.ForApproval(user, Request);
                eventAggregator.Publish(new UserEvent.ConfirmedEmail(user, approvalLink));
            }
        }

        await FinalizeInvitationIfApplicable(user);

        var response = new AcceptInviteResult
        {
            Email = user.Email!,
            EmailHasBeenConfirmed = emailHasBeenConfirmed,
            RequiresUserApproval = requiresUserApproval,
            PasswordSetCode = passwordSetCode
        };
        return Ok(response);
    }

    [HttpPost("switch-mode")]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    [Authorize(Policy = Policies.CanModifyProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> SwitchMode(SwitchModeRequest req)
    {
        if (string.IsNullOrEmpty(req.StoreId))
            ModelState.AddModelError(nameof(req.StoreId), "Missing store id");
        var permissions = req.Mode switch
        {
            "Cashier" => new [] { Policies.CanViewProfile, Permission.Create(Policies.CanModifyInvoices, req.StoreId).ToString() },
            _ => null
        };
        if (permissions == null)
            ModelState.AddModelError(nameof(req.Mode), "Missing or invalid mode");
        if (!ModelState.IsValid)
            return this.CreateValidationError(ModelState);

        var user = await userManager.GetUserAsync(User);
        if (user == null)
            return this.CreateAPIError(404, "user-not-found", "The user was not found");

        var authorization = await authService.AuthorizeAsync(User, Policies.CanModifyStoreSettings);
        if (!authorization.Succeeded)
            return this.CreateAPIError(401, "unauthenticated", "Only store owners can switch modes");

        var identifier = $"BTCPay App: {req.Mode}";
        var key = await GetOrCreateApiKey(user.Id, identifier, permissions!);

        return Ok(new AuthenticationResponse { AccessToken = key.Id });
    }

    [HttpPost("logout")]
    [Authorize(Policy = Policies.CanModifyProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> Logout()
    {
        var user = await userManager.GetUserAsync(User);
        if (user != null)
        {
            await signInManager.SignOutAsync();
            _logger.LogInformation("User {Email} logged out", user.Email);
            return Ok();
        }
        return Unauthorized();
    }

    [HttpGet("user")]
    [Authorize(Policy = Policies.CanViewProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> UserInfo()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        var info = await GetUserInfo(user);
        return Ok(info);
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [RateLimitsFilter(ZoneLimits.ForgotPassword, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> ForgotPassword(ResetPasswordRequest resetRequest)
    {
        var email = resetRequest.Email;
        if (string.IsNullOrEmpty(email))
            ModelState.AddModelError(nameof(email), "Missing email");
        if (!ModelState.IsValid)
            return this.CreateValidationError(ModelState);

        var user = await userManager.FindByEmailAsync(email!);
        if (UserService.TryCanLogin(user, out _))
        {
            var callbackUri = await callbackGenerator.ForPasswordReset(user, Request);
            eventAggregator.Publish(new UserEvent.PasswordResetRequested(user, callbackUri));
        }
        return Ok();
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> SetPassword(ResetPasswordRequest resetRequest)
    {
        if (string.IsNullOrEmpty(resetRequest.Email))
            ModelState.AddModelError(nameof(resetRequest.Email), "Missing email");
        if (string.IsNullOrEmpty(resetRequest.ResetCode))
            ModelState.AddModelError(nameof(resetRequest.ResetCode), "Missing reset code");
        if (string.IsNullOrEmpty(resetRequest.NewPassword))
            ModelState.AddModelError(nameof(resetRequest.NewPassword), "Missing new password");
        if (!ModelState.IsValid)
            return this.CreateValidationError(ModelState);

        var user = await userManager.FindByEmailAsync(resetRequest.Email!);
        var needsInitialPassword = user != null && !await userManager.HasPasswordAsync(user);
        // Let unapproved users set a password. Otherwise, don't reveal that the user does not exist.
        if (!UserService.TryCanLogin(user, out var message) && !needsInitialPassword || user == null)
        {
            _logger.LogWarning("User {Email} tried to reset password, but failed: {Message}", user?.Email ?? "(NO EMAIL)", message);
            return this.CreateAPIError(401, "unauthenticated", "Invalid request");
        }

        IdentityResult result;
        try
        {
            result = await userManager.ResetPasswordAsync(user, resetRequest.ResetCode!, resetRequest.NewPassword!);
        }
        catch (FormatException)
        {
            result = IdentityResult.Failed(userManager.ErrorDescriber.InvalidToken());
        }

        if (!result.Succeeded) return this.CreateAPIError(401, "unauthenticated", result.ToString().Split(": ").Last());

        if (!needsInitialPassword) await FinalizeInvitationIfApplicable(user);

        // see if we can sign in user after accepting an invitation and setting the password
        if (needsInitialPassword && UserService.TryCanLogin(user, out _))
        {
            var signInResult = await signInManager.PasswordSignInAsync(user.Email!, resetRequest.NewPassword!, true, true);
            if (signInResult.Succeeded)
            {
                _logger.LogInformation("User {Email} logged in", user.Email);
                return await UserAuthenticated(user);
            }
        }

        return Ok();
    }

    private async Task<IActionResult> UserAuthenticated(ApplicationUser user)
    {
        const string identifier = "BTCPay App";
        var key = await GetOrCreateApiKey(user.Id, identifier, [Policies.Unrestricted]);
        return Ok(new AuthenticationResponse { AccessToken = key.Id });
    }

    private async Task<APIKeyData> GetOrCreateApiKey(string userId, string identifier, string[] permissions)
    {
        var keys = await apiKeyRepository.GetKeys(new APIKeyRepository.APIKeyQuery { UserId = [userId] });
        var key = keys.FirstOrDefault(k =>
        {
            var blob = k.GetBlob<APIKeyBlob>();
            return blob != null && blob.ApplicationIdentifier == identifier && blob.Permissions.SequenceEqual(permissions);
        });
        if (key == null)
        {
            key = new APIKeyData
            {
                Id = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20)),
                Type = APIKeyType.Permanent,
                UserId = userId,
                Label = identifier
            };
            key.SetBlob(new APIKeyBlob
            {
                ApplicationIdentifier = identifier,
                Permissions = permissions
            });
            await apiKeyRepository.CreateKey(key);
        }
        return key;
    }

    private async Task<AppUserInfo> GetUserInfo(ApplicationUser user)
    {
        var userStores = await storeRepository.GetStoresByUserId(user.Id);
        var apiKeyPermissions = HttpContext.GetPermissions();
        var isUnrestricted = apiKeyPermissions is [Policies.Unrestricted];
        var stores = new List<AppUserStoreInfo>();
        foreach (var store in userStores)
        {
            if (!HttpContext.HasPermission(Permission.Create(Policies.CanViewInvoices, store.Id))) continue;

            var userStore = store.UserStores.Find(us => us.ApplicationUserId == user.Id && us.StoreDataId == store.Id)!;
            var apps = await appService.GetAllApps(user.Id, false, store.Id);
            var posApp = apps.FirstOrDefault(app => app.AppType == PointOfSaleAppType.AppType && app.App.GetSettings<PointOfSaleSettings>().DefaultView == PosViewType.Light);
            var storeBlob = userStore.StoreData.GetStoreBlob();
            var storePermissions = isUnrestricted ? userStore.StoreRole.Permissions : apiKeyPermissions.Where(Policies.IsStorePolicy);
            stores.Add(new AppUserStoreInfo
            {
                Id = store.Id,
                Name = store.StoreName,
                Archived = store.Archived,
                RoleId = userStore.StoreRole.Id,
                PosAppId = posApp?.Id,
                Permissions = storePermissions,
                DefaultCurrency = storeBlob.DefaultCurrency,
                LogoUrl = storeBlob.LogoUrl != null
                    ? await uriResolver.Resolve(Request.GetAbsoluteRootUri(), storeBlob.LogoUrl)
                    : null,
            });
        }

        var userBlob = user.GetBlob<UserBlob>();
        var info = new AppUserInfo
        {
            UserId = user.Id,
            Name = userBlob?.Name,
            ImageUrl = !string.IsNullOrEmpty(userBlob?.ImageUrl)
                ? await uriResolver.Resolve(Request.GetAbsoluteRootUri(), UnresolvedUri.Create(userBlob.ImageUrl))
                : null,
            Email = await userManager.GetEmailAsync(user),
            Roles = isUnrestricted ? await userManager.GetRolesAsync(user) : [],
            Stores = stores
        };
        return info;
    }

    private async Task FinalizeInvitationIfApplicable(ApplicationUser user)
    {
        if (!userManager.HasInvitationToken<ApplicationUser>(user)) return;

        // This is a placeholder, the real storeIds will be set by the UserEventHostedService
        var storeUsersLink = callbackGenerator.StoreUsersLink("{0}", Request);
        eventAggregator.Publish(new UserEvent.InviteAccepted(user, storeUsersLink));
        // unset used token
        await userManager.UnsetInvitationTokenAsync<ApplicationUser>(user.Id);
    }
}
