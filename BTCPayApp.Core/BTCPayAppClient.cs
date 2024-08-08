using System.Net;
using System.Net.Http.Headers;
using System.Web;
using BTCPayApp.CommonServer.Models;
using BTCPayApp.Core.AspNetRip;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AccessTokenResponse = BTCPayApp.Core.AspNetRip.AccessTokenResponse;
using ProblemDetails = BTCPayApp.Core.AspNetRip.ProblemDetails;
using RefreshRequest = BTCPayApp.Core.AspNetRip.RefreshRequest;

namespace BTCPayApp.Core;

public class BTCPayAppClient(string baseUri, HttpClient client) : BTCPayServerClient(new Uri(baseUri), client)
{
    private const string RefreshPath = "btcpayapp/refresh";
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

    protected override async Task<T> HandleResponse<T>(HttpResponseMessage res)
    {
        if (res is { IsSuccessStatusCode: false })
        {
            var req = res.RequestMessage;
            if (res.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(RefreshToken))
            {
                // try refresh and recurse if the token could be renewed
                var uri = req!.RequestUri;
                var path = uri!.AbsolutePath;
                if (!path.EndsWith(RefreshPath))
                {
                    var (refresh, _) = await RefreshAccess(RefreshToken);
                    if (refresh != null)
                    {
                        if (req.Content is not null)
                        {
                            var content = await req.Content.ReadAsStringAsync();
                            var payload = JsonConvert.DeserializeObject<T>(content);
                            return await SendHttpRequest<T>(path, bodyPayload: payload, method: req.Method);
                        }

                        var query = HttpUtility.ParseQueryString(uri.Query);
                        var queryPayload = query.HasKeys() ? query.AllKeys.ToDictionary(k => k, k => query[k]) : null;
                        return await SendHttpRequest<T>(path, queryPayload, method: req.Method);
                    }
                }
                ClearAccess();
            }
            else
            {
                // try parsing as ProblemDetails
                try
                {
                    var content = await res.Content.ReadAsStringAsync();
                    var err = JsonConvert.DeserializeObject<ProblemDetails>(content);
                    if (err?.Status != null && !string.IsNullOrEmpty(err.Detail))
                    {
                        var error = new GreenfieldAPIError("unauthorized", err.Detail);
                        throw new GreenfieldAPIException(err.Status.Value, error);
                    }
                }
                catch (JsonSerializationException e)
                {
                    // ignored
                }
            }
        }
        return await base.HandleResponse<T>(res);
    }

    private AccessTokenResult HandleAccessTokenResponse(AccessTokenResponse response, DateTimeOffset expiryOffset)
    {
        var expiry = expiryOffset + TimeSpan.FromSeconds(response.ExpiresIn);
        SetAccess(response.AccessToken, response.RefreshToken, expiry);
        return new AccessTokenResult(response.AccessToken, response.RefreshToken, expiry);
    }

    public async Task<(AccessTokenResult? success, string? errorCode)> RefreshAccess(string? refreshToken = null, CancellationToken? cancellation = default)
    {
        var token = refreshToken ?? RefreshToken;
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("No refresh token present or provided.", nameof(refreshToken));

        var payload = new RefreshRequest { RefreshToken = token };
        var now = DateTimeOffset.Now;
        try
        {
            var tokenResponse = await SendHttpRequest<AccessTokenResponse>(RefreshPath, bodyPayload: payload, method: HttpMethod.Post);
            var res = HandleAccessTokenResponse(tokenResponse, now);
            AccessRefreshed?.Invoke(this, res);
            return (res, null);
        }
        catch (Exception e)
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

    public async Task<JObject> RegisterUser(SignupRequest payload, CancellationToken cancellation)
    {
        return await SendHttpRequest<JObject>("btcpayapp/register", payload, HttpMethod.Post, cancellation);
    }

    public async Task<AccessTokenResponse> Login(LoginRequest payload, CancellationToken cancellation)
    {
        return await SendHttpRequest<AccessTokenResponse>("btcpayapp/login", payload, HttpMethod.Post, cancellation);
    }

    public async Task<AccessTokenResponse> Login(string loginCode, CancellationToken cancellation)
    {
        return await SendHttpRequest<AccessTokenResponse>("btcpayapp/login/code", loginCode, HttpMethod.Post, cancellation);
    }

    public async Task<AcceptInviteResult> AcceptInvite(AcceptInviteRequest payload, CancellationToken cancellation)
    {
        return await SendHttpRequest<AcceptInviteResult>("btcpayapp/accept-invite", payload, HttpMethod.Post, cancellation);
    }

    public async Task ResetPassword(ResetPasswordRequest payload, CancellationToken cancellation)
    {
        var isForgotStep = string.IsNullOrEmpty(payload.ResetCode) && string.IsNullOrEmpty(payload.NewPassword);
        var path = isForgotStep ? "btcpayapp/forgot-password" : "btcpayapp/reset-password";
        await SendHttpRequest<EmptyResult>(path, payload, HttpMethod.Post, cancellation);
    }
}
public class EmptyResult { }
