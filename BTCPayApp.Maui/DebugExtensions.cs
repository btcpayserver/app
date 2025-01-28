//#if DEBUG

namespace BTCPayApp.Maui;

using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection.Extensions;

public class DangerousHttpClientFactory : IHttpClientFactory
{
    public static bool ServerValidate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        return true;
        if (errors == SslPolicyErrors.None) return true;
        return certificate?.Subject.Equals("CN=localhost") is true || certificate?.Issuer.Equals("CN=localhost") is true;
    }

    private static HttpClientHandler GetInsecureHandler()
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = ServerValidate;
        return handler;
    }

    public HttpClient CreateClient(string name)
    {
        return new HttpClient(GetInsecureHandler());
    }
}

#if ANDROID
public class DangerousAndroidMessageHandler : Xamarin.Android.Net.AndroidMessageHandler
{
    protected override Javax.Net.Ssl.IHostnameVerifier GetSSLHostnameVerifier(Javax.Net.Ssl.HttpsURLConnection connection)
        => new CustomHostnameVerifier();

    private sealed class CustomHostnameVerifier : Java.Lang.Object, Javax.Net.Ssl.IHostnameVerifier
    {
        public bool Verify(string? hostname, Javax.Net.Ssl.ISSLSession? session)
        {
            return true;//session?.PeerPrincipal?.Name == "CN=localhost";
        }
    }
}
#endif

public static class DebugExtensions
{
    public static IServiceCollection AddDangerousSSLSettingsForDev(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IHttpClientFactory, DangerousHttpClientFactory>());

        services.AddSingleton<Func<HttpMessageHandler, HttpMessageHandler>>(handler =>
        {
            if (handler is HttpClientHandler clientHandler)
            {
                // always verify the SSL certificate
                clientHandler.ServerCertificateCustomValidationCallback += DangerousHttpClientFactory.ServerValidate;

                return clientHandler;
            }
#if ANDROID
            return new DangerousAndroidMessageHandler();
#else
            return handler;
#endif
        });

        services.AddSingleton<Action<ClientWebSocketOptions>>(provider => wsc =>
        {
            wsc.RemoteCertificateValidationCallback = DangerousHttpClientFactory.ServerValidate;
        });
        return services;
    }
}
//#endif
