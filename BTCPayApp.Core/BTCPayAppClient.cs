using System.Globalization;
using System.Net.Http.Headers;
using BTCPayServer.Client;
using BTCPayServer.Client.App.Models;
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

    public async Task<AuthenticationResponse> Login(LoginRequest payload, CancellationToken cancellation = default)
    {
        return await SendHttpRequest<AuthenticationResponse>("btcpayapp/login", payload, HttpMethod.Post, cancellation);
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

    public async Task<JObject?> CreatePosInvoice(CreatePosInvoiceRequest req, CancellationToken cancellation = default)
    {
        var query = new Dictionary<string, object>();
        if (req.Total != null) query.Add("amount", req.Total.Value.ToString(CultureInfo.InvariantCulture));
        if (req.DiscountPercent != null) query.Add("discount", req.DiscountPercent.Value.ToString(CultureInfo.InvariantCulture));
        if (req.Tip != null) query.Add("tip", req.Tip.Value.ToString(CultureInfo.InvariantCulture));
        if (req.PosData != null) query.Add("posData", req.PosData);
        return await SendHttpRequest<JObject?>($"apps/{req.AppId}/pos/light", query, HttpMethod.Post, cancellation);
    }
}
