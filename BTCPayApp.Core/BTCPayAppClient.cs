using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BTCPayApp.CommonServer;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayApp.Core;

public class BTCPayAppClient(IHttpClientFactory clientFactory)
{
    private readonly HttpClient _httpClient = clientFactory.CreateClient();
    private readonly string[] _unauthenticatedPaths = ["instance", "login", "register", "forgot-password", "reset-password"];
    private DateTimeOffset? AccessExpiry { get; set; } // TODO: Incorporate in refresh check
    private string? AccessToken { get; set; }
    private string? RefreshToken { get; set; }

    public event EventHandler<AccessTokenResult>? AccessRefreshed;

    public async Task<TResponse> Get<TResponse>(string baseUrl, string path, CancellationToken cancellation = default, bool isRetry = false)
    {
        return await Send<EmptyRequestModel, TResponse>(HttpMethod.Get, baseUrl, path, null, cancellation, isRetry);
    }

    public async Task Post<TRequest>(string baseUrl, string path, TRequest payload, CancellationToken cancellation = default, bool isRetry = false)
    {
        await Send<TRequest, EmptyResponseModel>(HttpMethod.Post, baseUrl, path, payload, cancellation, isRetry);
    }

    public async Task<TResponse> Post<TRequest, TResponse>(string baseUrl, string path, TRequest payload, CancellationToken cancellation = default, bool isRetry = false)
    {
        return await Send<TRequest, TResponse>(HttpMethod.Post, baseUrl, path, payload, cancellation, isRetry);
    }

    private async Task<TResponse> Send<TRequest, TResponse>(HttpMethod method, string baseUrl, string path, TRequest? payload, CancellationToken cancellation, bool isRetry = false)
    {
        var req = new HttpRequestMessage
        {
            RequestUri = new Uri(WithTrailingSlash(baseUrl) + $"btcpayapp/{path}"),
            Method = method,
            Content = payload == null ? null : JsonContent.Create(payload)
        };
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("User-Agent", "BTCPayServerAppApiClient");

        if (!_unauthenticatedPaths.Contains(path))
        {
            if (string.IsNullOrEmpty(AccessToken))
                throw new BTCPayAppClientException(401, "Authentication required");

            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        var res = await _httpClient.SendAsync(req, cancellation);
        if (!res.IsSuccessStatusCode)
        {
            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                // try refresh and recurse if the token is expired
                if (!string.IsNullOrEmpty(RefreshToken) && !isRetry)
                {
                    var (refresh, _) = await Refresh(baseUrl, RefreshToken, cancellation);
                    if (refresh != null) return await Send<TRequest, TResponse>(method, baseUrl, path, payload, cancellation);
                }

                ClearAccess();
            }
            // otherwise handle the error response
            var problem = res.StatusCode == HttpStatusCode.NotFound
                ? new ProblemDetails { Instance = baseUrl, Status = 404, Detail = "This server does not seem to support the BTCPay app." }
                : await res.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: cancellation);
            var statusCode = problem?.Status ?? (int)res.StatusCode;
            var message = problem?.Detail ?? res.ReasonPhrase;
            throw new BTCPayAppClientException(statusCode, message ?? "Request failed");
        }

        if (typeof(TResponse) == typeof(EmptyResponseModel))
        {
            return (TResponse)(object)new EmptyResponseModel();
        }

        var response = await res.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellation);
        return response != null ? response : (TResponse)(object)new EmptyResponseModel();
    }

    private AccessTokenResult HandleAccessTokenResponse(AccessTokenResponse response, DateTimeOffset expiryOffset)
    {
        var expiry = expiryOffset + TimeSpan.FromSeconds(response.ExpiresIn);
        SetAccess(response.AccessToken, response.RefreshToken, expiry);
        return new AccessTokenResult(response.AccessToken, response.RefreshToken, expiry);
    }

    private async Task<(AccessTokenResult? success, string? errorCode)> Refresh(string serverUrl, string refreshToken, CancellationToken? cancellation = default)
    {
        var payload = new RefreshRequest { RefreshToken = refreshToken };
        var now = DateTimeOffset.Now;
        try
        {
            var response = await Post<RefreshRequest, AccessTokenResponse>(serverUrl, "refresh", payload, cancellation.GetValueOrDefault(), true);
            var res = HandleAccessTokenResponse(response, now);
            AccessRefreshed?.Invoke(this, res);
            return (res, null);
        }
        catch (BTCPayAppClientException e)
        {
            return (null, e.Message);
        }
    }

    public void ClearAccess()
    {
        AccessToken = RefreshToken = null;
        AccessExpiry = null;
    }

    public void SetAccess(string accessToken, string refreshToken, DateTimeOffset expiry)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        AccessExpiry = expiry;
    }

    private static string WithTrailingSlash(string str) => str.EndsWith('/') ? str : str + "/";

    private class EmptyRequestModel;
    private class EmptyResponseModel;
}
