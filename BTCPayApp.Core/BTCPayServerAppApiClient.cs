using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BTCPayApp.CommonServer;
using BTCPayApp.Core.Contracts;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayApp.Core;

public class BTCPayServerAppApiClient(IHttpClientFactory clientFactory, ISecureConfigProvider secureConfigProvider)
{
    private readonly HttpClient _httpClient = clientFactory.CreateClient();
    private readonly string[] _unauthenticatedPaths = ["login", "forgot-password", "reset-password"];

    private BTCPayServerAccount? Account { get; set; }

    public async Task<(AccessTokenResult? success, string? errorCode)> Login(BTCPayServerAccount account, string password, string? otp = null, CancellationToken? cancellation = default)
    {
        Account = account;
        var payload = new LoginRequest
        {
            Email = Account.Email,
            Password = password,
            TwoFactorCode = otp
        };
        try
        {
            var now = DateTimeOffset.Now;
            var response = await Post<LoginRequest, AccessTokenResponse>("login", payload, cancellation.GetValueOrDefault());
            var res = await HandleAccessTokenResponse(response!, now);
            return (res, null);
        }
        catch (BTCPayServerClientException e)
        {
            return (null, e.Message);
        }
    }

    private async Task<(AccessTokenResult? success, string? errorCode)> Refresh(BTCPayServerAccount account, CancellationToken? cancellation = default)
    {
        if (string.IsNullOrEmpty(account.RefreshToken)) throw new BTCPayServerClientException(422, "Account or Refresh Token missing");

        Account = account;
        var payload = new RefreshRequest
        {
            RefreshToken = Account.RefreshToken
        };
        try
        {
            var now = DateTimeOffset.Now;
            var response = await Post<RefreshRequest, AccessTokenResponse>("refresh", payload, cancellation.GetValueOrDefault());
            var res = await HandleAccessTokenResponse(response!, now);
            return (res, null);
        }
        catch (BTCPayServerClientException e)
        {
            return (null, e.Message);
        }
    }

    public async Task<AppUserInfoResponse?> GetUserInfo(BTCPayServerAccount account, CancellationToken? cancellation = default)
    {
        Account = account;
        return await Get<AppUserInfoResponse>("info", cancellation.GetValueOrDefault());
    }

    public async Task<(bool success, string? errorCode)> ResetPassword(BTCPayServerAccount account, string? resetCode = null, string? newPassword = null, CancellationToken? cancellation = default)
    {
        Account = account;
        var payload = new ResetPasswordRequest
        {
            Email = Account.Email,
            ResetCode = resetCode ?? string.Empty,
            NewPassword = newPassword ?? string.Empty
        };
        try
        {
            var path = string.IsNullOrEmpty(payload.ResetCode) && string.IsNullOrEmpty(payload.NewPassword)
                ? "forgot-password"
                : "reset-password";
            await Post<ResetPasswordRequest, EmptyResponseModel>(path, payload, cancellation.GetValueOrDefault());
            return (true, null);
        }
        catch (BTCPayServerClientException e)
        {
            return (false, e.Message);
        }
    }

    public void Logout()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    private async Task<TResponse?> Get<TResponse>(string path, CancellationToken cancellation = default)
    {
        return await Send<EmptyRequestModel, TResponse>(HttpMethod.Get, path, null, cancellation);
    }

    private async Task<TResponse?> Post<TRequest, TResponse>(string path, TRequest payload, CancellationToken cancellation = default)
    {
        return await Send<TRequest, TResponse>(HttpMethod.Post, path, payload, cancellation);
    }

    private async Task<TResponse?> Send<TRequest, TResponse>(HttpMethod method, string path, TRequest? payload, CancellationToken cancellation, bool isRetry = false)
    {
        if (string.IsNullOrEmpty(Account?.BaseUri.ToString())) throw new BTCPayServerClientException(422, "Account or Server URL missing");

        var req = new HttpRequestMessage
        {
            RequestUri = new Uri($"{Account.BaseUri}btcpayapp/{path}"),
            Method = method,
            Content = payload == null ? null : JsonContent.Create(payload)
        };
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("User-Agent", "BTCPayServerAppApiClient");

        if (!_unauthenticatedPaths.Contains(path) && !string.IsNullOrEmpty(Account.AccessToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Account.AccessToken);
        }

        var res = await _httpClient.SendAsync(req, cancellation);
        if (!res.IsSuccessStatusCode)
        {
            // try refresh and recurse if the token is expired
            if (res.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(Account.RefreshToken) && !isRetry)
            {
                var (refresh, _) = await Refresh(Account, cancellation);
                if (refresh != null) return await Send<TRequest, TResponse>(method, path, payload, cancellation);
            }
            // otherwise handle the error response
            var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: cancellation);
            var statusCode = problem?.Status ?? (int)res.StatusCode;
            var message = problem?.Detail ?? res.ReasonPhrase;
            throw new BTCPayServerClientException(statusCode, message ?? "Request failed");
        }

        if (typeof(TResponse) == typeof(EmptyResponseModel))
        {
            return (TResponse)(object)new EmptyResponseModel();
        }
        return await res.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellation);
    }

    private class EmptyRequestModel;
    private class EmptyResponseModel;

    private class BTCPayServerClientException(int statusCode, string message) : Exception
    {
        public int StatusCode { get; init; } = statusCode;
        public override string Message => message;
    }

    private async Task<AccessTokenResult?> HandleAccessTokenResponse(AccessTokenResponse response, DateTimeOffset expiryOffset)
    {
        var expiry = expiryOffset + TimeSpan.FromSeconds(response.ExpiresIn);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(response.TokenType, response.AccessToken);
        Account!.SetAccess(response.AccessToken, response.RefreshToken, expiry);
        await secureConfigProvider.Set("account", Account);
        return new AccessTokenResult(response.AccessToken, response.RefreshToken, expiry);
    }
}
