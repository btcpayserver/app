using System.Security.Claims;
using BTCPayApp.Core;
using Microsoft.AspNetCore.Components.Authorization;

namespace BTCPayApp.UI;

public class AuthStateProvider : AuthenticationStateProvider
{
    public BTCPayServerAccount? Account { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var identity = new ClaimsIdentity();
        if (Account != null)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Uri, Account.BaseUri.AbsoluteUri),
                new Claim(ClaimTypes.Name, Account.Email),
                new Claim(ClaimTypes.Email, Account.Email),
                new Claim("AccessToken", Account.AccessToken),
                new Claim("RefreshToken", Account.RefreshToken)
            };
            identity = new ClaimsIdentity(claims, "Bearer");
        }
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    public bool SetAccount(BTCPayServerAccount? account)
    {
        if (account == null || string.IsNullOrEmpty(account.AccessToken) || string.IsNullOrEmpty(account.RefreshToken))
        {
            Logout();
            return false;
        }

        Account = account;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        return true;
    }

    public void Logout()
    {
        Account = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
