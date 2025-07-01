using System.Globalization;
using System.Net.Http.Headers;
using BTCPayApp.Core.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayApp.Core;

public class BTCPayAppClient(string baseUri, string? apiKey = null, HttpClient? client = null) : BTCPayServerClient(new Uri(baseUri), apiKey, client)
{
    protected override HttpRequestMessage CreateHttpRequest(string path, Dictionary<string, object>? queryPayload = null, HttpMethod? method = null)
    {
        var req = base.CreateHttpRequest(path, queryPayload, method);
        req.Headers.Add("User-Agent", "BTCPayAppClient");
        req.Headers.Add("Accept", "application/json");
        return req;
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

    public async Task<JObject> RegisterUser(CreateApplicationUserRequest payload, CancellationToken cancellation = default)
    {
        return await SendHttpRequest<JObject>("btcpayapp/register", payload, HttpMethod.Post, cancellation);
    }

    public async Task<LoginInfoResult> LoginInfo(string email, CancellationToken cancellation = default)
    {
        var payload = new Dictionary<string, object> { { "email", email } };
        return await SendHttpRequest<LoginInfoResult>("btcpayapp/login-info", payload, HttpMethod.Get, cancellation);
    }

    public async Task<AuthenticationResponse> Login(LoginRequest payload, CancellationToken cancellation = default)
    {
        return await SendHttpRequest<AuthenticationResponse>("btcpayapp/login", payload, HttpMethod.Post, cancellation);
    }

    public async Task<AuthenticationResponse> SwitchMode(SwitchModeRequest payload, CancellationToken cancellation = default)
    {
        return await SendHttpRequest<AuthenticationResponse>("btcpayapp/switch-mode", payload, HttpMethod.Post, cancellation);
    }

    public async Task<AuthenticationResponse> Login(string loginCode, CancellationToken cancellation = default)
    {
        return await SendHttpRequest<AuthenticationResponse>("btcpayapp/login/code", loginCode, HttpMethod.Post, cancellation);
    }

    public async Task<AcceptInviteResult> AcceptInvite(AcceptInviteRequest payload, CancellationToken cancellation = default)
    {
        return await SendHttpRequest<AcceptInviteResult>("btcpayapp/accept-invite", payload, HttpMethod.Post, cancellation);
    }

    public async Task<JObject?> ResetPassword(ResetPasswordRequest payload, CancellationToken cancellation = default)
    {
        var isForgotStep = string.IsNullOrEmpty(payload.ResetCode) && string.IsNullOrEmpty(payload.NewPassword);
        var path = isForgotStep ? "btcpayapp/forgot-password" : "btcpayapp/reset-password";
        return await SendHttpRequest<JObject?>(path, payload, HttpMethod.Post, cancellation);
    }

    public async Task<JObject?> CreatePosInvoice(Models.CreatePosInvoiceRequest req, CancellationToken cancellation = default)
    {
        var query = new Dictionary<string, object>();
        if (req.DiscountPercent != null) query.Add("discount", req.DiscountPercent.Value.ToString(CultureInfo.InvariantCulture));
        if (req.Tip != null) query.Add("tip", req.Tip.Value.ToString(CultureInfo.InvariantCulture));
        if (req.PosData != null) query.Add("posData", req.PosData);
        return await SendHttpRequest<JObject?>($"apps/{req.AppId}/pos/light", query, HttpMethod.Post, cancellation);
    }

    public async Task<string> SubmitLNURLWithdrawForInvoice(SubmitLnUrlRequest req, CancellationToken cancellation = default)
    {
        return await SendHttpRequest<string>($"plugins/NFC", req, HttpMethod.Post, cancellation);
    }

    public virtual async Task<T> UploadFileRequest<T>(string apiPath, StreamContent fileContent, string fileName, string mimeType, CancellationToken token = default)
    {
        using MultipartFormDataContent multipartContent = new();
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
        multipartContent.Add(fileContent, "file", fileName);
        var req = CreateHttpRequest(apiPath, null, HttpMethod.Post);
        req.Content = multipartContent;
        using var resp = await _httpClient.SendAsync(req, token);
        return await HandleResponse<T>(resp);
    }
}
