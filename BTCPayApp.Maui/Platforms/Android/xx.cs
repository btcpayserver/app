#if DEBUG // Ensure this never leaves debug stages.
using System.Reflection;
using System.Reflection.Emit;
using Javax.Net.Ssl;
using Xamarin.Android.Net;
using Java.Net;
using Java.Security;
using Java.Security.Cert;

namespace BTCPayApp.Maui;

internal static class DangerousAndroidMessageHandlerEmitter
{
    private const string NAME = "DangerousAndroidMessageHandler";

    private static Assembly? EmittedAssembly { get; set; } = null;

    public static void Register(string handlerName = NAME, string assemblyName = NAME) =>
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            assemblyName.Equals(args.Name)
                ? (EmittedAssembly ??= Emit(handlerName, assemblyName))
                : null;

    private static AssemblyBuilder Emit(string handlerName, string assemblyName)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        var builder = assembly.DefineDynamicModule(assemblyName)
            .DefineType(handlerName, TypeAttributes.Public);
        builder.SetParent(typeof(AndroidMessageHandler));
        builder.DefineDefaultConstructor(MethodAttributes.Public);

        var generator = builder.DefineMethod(
                "GetSSLHostnameVerifier",
                MethodAttributes.Public | MethodAttributes.Virtual,
                typeof(IHostnameVerifier),
                new[] {typeof(HttpsURLConnection)})
            .GetILGenerator();
        generator.Emit(
            OpCodes.Call,
            typeof(DangerousHostNameVerifier)
                .GetMethod(nameof(DangerousHostNameVerifier.Create))!);
        generator.Emit(OpCodes.Ret);

        builder.CreateType();

        return assembly;
    }

    public class DangerousHostNameVerifier : Java.Lang.Object, IHostnameVerifier
    {
        public bool Verify(string? hostname, ISSLSession? session) => true;

        public static IHostnameVerifier Create() => new DangerousHostNameVerifier();
    }
}

internal class DangerousTrustProvider : Provider
{
    private const string DANGEROUS_ALGORITHM = nameof(DANGEROUS_ALGORITHM);

    // NOTE: Empty ctor, i. e. without Put(), works for me as well,
    // but I'll keep it for the sake of completeness.
    public DangerousTrustProvider()
        : base(nameof(DangerousTrustProvider), 1, "Dangerous debug TrustProvider") =>
        Put(
            $"{nameof(DangerousTrustManagerFactory)}.{DANGEROUS_ALGORITHM}",
            Java.Lang.Class.FromType(typeof(DangerousTrustManagerFactory)).Name);

    public static void Register()
    {
        if (Security.GetProvider(nameof(DangerousTrustProvider)) is null)
        {
            Security.InsertProviderAt(new DangerousTrustProvider(), 1);
            Security.SetProperty(
                $"ssl.{nameof(DangerousTrustManagerFactory)}.algorithm", DANGEROUS_ALGORITHM);
        }
    }

    public class DangerousTrustManager : X509ExtendedTrustManager
    {
        public override void CheckClientTrusted(X509Certificate[]? chain, string? authType)
        {
        }

        public override void CheckClientTrusted(X509Certificate[]? chain, string? authType,
            Socket? socket)
        {
        }

        public override void CheckClientTrusted(X509Certificate[]? chain, string? authType,
            SSLEngine? engine)
        {
        }

        public override void CheckServerTrusted(X509Certificate[]? chain, string? authType)
        {
        }

        public override void CheckServerTrusted(X509Certificate[]? chain, string? authType,
            Socket? socket)
        {
        }

        public override void CheckServerTrusted(X509Certificate[]? chain, string? authType,
            SSLEngine? engine)
        {
        }

        public override X509Certificate[] GetAcceptedIssuers() =>
            Array.Empty<X509Certificate>();
    }

    public class DangerousTrustManagerFactory : TrustManagerFactorySpi
    {
        protected override ITrustManager[] EngineGetTrustManagers() =>
            new[] {new DangerousTrustManager()};

        protected override void EngineInit(IManagerFactoryParameters? parameters)
        {
        }

        protected override void EngineInit(KeyStore? store)
        {
        }
    }
}
#endif