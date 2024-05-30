using System.Net;
using System.Net.Http.Headers;
using BTCPayApp.CommonServer.Models;
using BTCPayApp.Core.AspNetRip;
using BTCPayServer.Client;
using AccessTokenResponse = BTCPayApp.Core.AspNetRip.AccessTokenResponse;
using RefreshRequest = BTCPayApp.Core.AspNetRip.RefreshRequest;

namespace BTCPayApp.Core;

public class BTCPayAppClient(string baseUri) : BTCPayServerClient(new Uri(baseUri))
{
    private const string RetryHeaderName = "X-Retry";
    private DateTimeOffset? AccessExpiry { get; set; } // TODO: Incorporate in refresh check
    private string? AccessToken { get; set; }
    private string? RefreshToken { get; set; }

    public event EventHandler<AccessTokenResult>? AccessRefreshed;

    public void SetAccess(string accessToken, string refreshToken, DateTimeOffset expiry)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        AccessExpiry = expiry;
    }

    private void ClearAccess()
    {
        AccessToken = RefreshToken = null;
        AccessExpiry = null;
    }

    protected override HttpRequestMessage CreateHttpRequest(string path, Dictionary<string, object>? queryPayload = null, HttpMethod? method = null)
    {
        var req = base.CreateHttpRequest(path, queryPayload, method);
        req.Headers.Add("User-Agent", "BTCPayAppClient");
        if (!string.IsNullOrEmpty(AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return req;
    }

    protected override async Task<T> SendHttpRequest<T>(string path, Dictionary<string, object>? queryPayload = null, HttpMethod? method = null, CancellationToken cancellationToken = default)
    {
        var req = CreateHttpRequest(path, queryPayload, method);
        using var res = await _httpClient.SendAsync(req, cancellationToken);
        return await HandleResponse<T>(res, path, queryPayload, method, cancellationToken);
    }

    protected override async Task<T> SendHttpRequest<T>(string path, object? bodyPayload = null, HttpMethod? method = null, CancellationToken cancellationToken = default)
    {
        var req = CreateHttpRequest(path, bodyPayload: bodyPayload, method: method);
        using var res = await _httpClient.SendAsync(req, cancellationToken);
        return await HandleResponse<T>(res, path, bodyPayload: bodyPayload, method: method, cancellationToken);
    }

    private async Task<T> HandleResponse<T>(HttpResponseMessage res, string path, Dictionary<string, object>? queryPayload = null, HttpMethod? method = null, CancellationToken cancellationToken = default)
    {
        if (res is { IsSuccessStatusCode: false, StatusCode: HttpStatusCode.Unauthorized } && !string.IsNullOrEmpty(RefreshToken))
        {
            // try refresh and recurse if the token could be renewed
            if (res.RequestMessage?.Headers.Contains(RetryHeaderName) is not true)
            {
                var (refresh, _) = await Refresh(RefreshToken);
                if (refresh != null)
                {
                    return await SendHttpRequest<T>(path, queryPayload, method: method, cancellationToken: cancellationToken);
                }
            }

            ClearAccess();
        }
        return await base.HandleResponse<T>(res);
    }

    private async Task<T> HandleResponse<T>(HttpResponseMessage res, string path, object? bodyPayload = null, HttpMethod? method = null, CancellationToken cancellationToken = default)
    {
        if (res is { IsSuccessStatusCode: false, StatusCode: HttpStatusCode.Unauthorized } && !string.IsNullOrEmpty(RefreshToken))
        {
            // try refresh and recurse if the token could be renewed
            if (res.RequestMessage?.Headers.Contains(RetryHeaderName) is not true)
            {
                var (refresh, _) = await Refresh(RefreshToken);
                if (refresh != null)
                {
                    return await SendHttpRequest<T>(path, bodyPayload: bodyPayload, method: method, cancellationToken: cancellationToken);
                }
            }

            ClearAccess();
        }
        return await base.HandleResponse<T>(res);
    }

    private AccessTokenResult HandleAccessTokenResponse(AccessTokenResponse response, DateTimeOffset expiryOffset)
    {
        var expiry = expiryOffset + TimeSpan.FromSeconds(response.ExpiresIn);
        SetAccess(response.AccessToken, response.RefreshToken, expiry);
        return new AccessTokenResult(response.AccessToken, response.RefreshToken, expiry);
    }

    private async Task<(AccessTokenResult? success, string? errorCode)> Refresh(string refreshToken, CancellationToken? cancellation = default)
    {
        var payload = new RefreshRequest { RefreshToken = refreshToken };
        var now = DateTimeOffset.Now;
        try
        {
            var req = CreateHttpRequest("btcpayapp/refresh", bodyPayload: payload, method: HttpMethod.Post);
            req.Headers.Add(RetryHeaderName, "true");
            using var resp = await _httpClient.SendAsync(req, cancellation.GetValueOrDefault());
            var tokenResponse = await HandleResponse<AccessTokenResponse>(resp);
            var res = HandleAccessTokenResponse(tokenResponse, now);
            AccessRefreshed?.Invoke(this, res);
            return (res, null);
        }
        catch (BTCPayAppClientException e)
        {
            return (null, e.Message);
        }
    }

    public async Task<AppInstanceInfo?> GetInstanceInfo(CancellationToken cancellation = default)
    {
        return await SendHttpRequest<AppInstanceInfo>("btcpayapp/instance", null, HttpMethod.Get, cancellation);
    }

    public async Task<AppUserInfo?> GetUserInfo(CancellationToken cancellation = default)
    {
        return await SendHttpRequest<AppUserInfo>("btcpayapp/user", null, HttpMethod.Get, cancellation);
    }

    public async Task<CreateStoreData?> GetCreateStore(CancellationToken cancellation = default)
    {
        return await SendHttpRequest<CreateStoreData>("btcpayapp/create-store", null, HttpMethod.Get, cancellation);
    }

    public async Task<SignupResult> RegisterUser(SignupRequest payload, CancellationToken cancellation)
    {
        return await SendHttpRequest<SignupResult>("btcpayapp/register", payload, HttpMethod.Post, cancellation);
    }

    public async Task<AccessTokenResponse> Login(LoginRequest payload, CancellationToken cancellation)
    {
        return await SendHttpRequest<AccessTokenResponse>("btcpayapp/login", payload, HttpMethod.Post, cancellation);
    }

    public async Task ResetPassword(ResetPasswordRequest payload, CancellationToken cancellation)
    {
        var isForgotStep = string.IsNullOrEmpty(payload.ResetCode) && string.IsNullOrEmpty(payload.NewPassword);
        var path = isForgotStep ? "btcpayapp/forgot-password" : "btcpayapp/reset-password";
        await SendHttpRequest<AccessTokenResponse>(path, payload, HttpMethod.Post, cancellation);
    }
}
