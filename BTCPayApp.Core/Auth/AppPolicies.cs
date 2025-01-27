namespace BTCPayApp.Core.Auth;

public class AppPolicies
{
    public const string CanModifySettings = "btcpay.plugin.app.canmodifysettings";

    public static IEnumerable<string> AllPolicies
    {
        get
        {
            yield return CanModifySettings;
        }
    }
}
