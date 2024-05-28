using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BTCPayApp.CommonServer.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Newtonsoft.Json;
using AccessTokenResponse = BTCPayApp.Core.AspNetRip.AccessTokenResponse;
using RefreshRequest = BTCPayApp.Core.AspNetRip.RefreshRequest;

namespace BTCPayApp.Core;

public class BTCPayAppClient(IHttpClientFactory clientFactory)
{
    private const string MediaType = "application/json";
    private readonly HttpClient _httpClient = clientFactory.CreateClient();
    private readonly string[] _unauthenticatedPaths = ["btcpayapp/instance", "btcpayapp/login", "btcpayapp/register", "btcpayapp/forgot-password", "btcpayapp/reset-password"];
    private DateTimeOffset? AccessExpiry { get; set; } // TODO: Incorporate in refresh check
    private string? AccessToken { get; set; }
    private string? RefreshToken { get; set; }
    public BTCPayServerClient? GreenfieldClient(Uri uri)
    {
            if (AccessToken is null)
            {
                return null;
            }
            var httpClient = clientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            return new BTCPayServerClient(uri,httpClient);

        
    }

    public event EventHandler<AccessTokenResult>? AccessRefreshed;

    public async Task<TResponse> Get<TResponse>(string baseUrl, string path, CancellationToken cancellation = default)
    {
        return await Send<EmptyRequestModel, TResponse>(HttpMethod.Get, baseUrl, path, null, cancellation);
    }

    public async Task Post<TRequest>(string baseUrl, string path, TRequest payload, CancellationToken cancellation = default)
    {
        await Send<TRequest, EmptyResponseModel>(HttpMethod.Post, baseUrl, path, payload, cancellation);
    }

    public async Task<TResponse> Post<TRequest, TResponse>(string baseUrl, string path, TRequest payload, CancellationToken cancellation = default)
    {
        return await Send<TRequest, TResponse>(HttpMethod.Post, baseUrl, path, payload, cancellation);
    }

    public async Task Put<TRequest>(string baseUrl, string path, TRequest payload, CancellationToken cancellation = default)
    {
        await Send<TRequest, EmptyResponseModel>(HttpMethod.Put, baseUrl, path, payload, cancellation);
    }

    public async Task<TResponse> Put<TRequest, TResponse>(string baseUrl, string path, TRequest payload, CancellationToken cancellation = default)
    {
        return await Send<TRequest, TResponse>(HttpMethod.Put, baseUrl, path, payload, cancellation);
    }

    public async Task Delete<TRequest>(string baseUrl, string path, TRequest payload, CancellationToken cancellation = default)
    {
        await Send<TRequest, EmptyResponseModel>(HttpMethod.Delete, baseUrl, path, payload, cancellation);
    }

    public async Task<TResponse> Delete<TRequest, TResponse>(string baseUrl, string path, TRequest payload, CancellationToken cancellation = default)
    {
        return await Send<TRequest, TResponse>(HttpMethod.Delete, baseUrl, path, payload, cancellation);
    }

    private async Task<TResponse> Send<TRequest, TResponse>(HttpMethod method, string baseUrl, string path, TRequest? payload, CancellationToken cancellation, bool isRetry = false)
    {
        var req = new HttpRequestMessage
        {
            RequestUri = new Uri(WithTrailingSlash(baseUrl) + path),
            Method = method,
            Content = payload == null ? null : new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, MediaType)
        };
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));
        req.Headers.Add("User-Agent", "BTCPayAppClient");

        if (!_unauthenticatedPaths.Contains(path))
        {
            if (string.IsNullOrEmpty(AccessToken))
                throw new BTCPayAppClientException(HttpStatusCode.Unauthorized, "Authentication required");

            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        var res = await _httpClient.SendAsync(req, cancellation);
        var str = await res.Content.ReadAsStringAsync(cancellation);
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
            if (res.StatusCode == HttpStatusCode.NotFound && path.StartsWith("btcpayapp"))
            {
                throw new BTCPayAppClientException(HttpStatusCode.NotFound, "This server does not seem to support the BTCPay app.");
            }

            if (res.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var validationErrors = JsonConvert.DeserializeObject<GreenfieldValidationError[]>(str);
                var message = string.Join(", ", validationErrors.Select(ve => $"{ve.Path}: {ve.Message}"));
                throw new BTCPayAppClientException(HttpStatusCode.UnprocessableEntity, message);
            }

            var err = JsonConvert.DeserializeObject<GreenfieldAPIError>(str);
            throw new BTCPayAppClientException(res.StatusCode, err?.Message ?? "Request failed");
        }

        if (typeof(TResponse) == typeof(EmptyResponseModel))
        {
            return (TResponse)(object)new EmptyResponseModel();
        }

        var response = JsonConvert.DeserializeObject<TResponse>(str);
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
            var response = await Send<RefreshRequest, AccessTokenResponse>(HttpMethod.Post, serverUrl, "btcpayapp/refresh", payload, cancellation.GetValueOrDefault(), true);
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


    public async Task<AppInstanceInfo?> GetInstanceInfo(string serverUrl)
    {
        return await Get<AppInstanceInfo>(serverUrl, "btcpayapp/instance");
    }

    private static string WithTrailingSlash(string str) => str.EndsWith('/') ? str : str + "/";

    private class EmptyRequestModel;
    private class EmptyResponseModel;
}
