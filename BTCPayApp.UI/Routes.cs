namespace BTCPayApp.UI;

public static class Routes
{
    // unauthorized
    public const string Index = "/";
    public const string Welcome = "/welcome";
    public const string Connect = "/connect";
    public const string Login = "/login";
    public const string Register = "/register";
    public const string ForgotPassword = "/forgot-password";

    // authorized
    public const string Dashboard = "/dashboard";
    public const string Notifications = "/notifications";
    public const string Settings = "/settings";
    public const string ChangePasscode = "/settings/passcode";
    public const string EnterPasscode = "/passcode";
    public const string Pair = "/pair";
    public const string Wallet = "/wallet";
    public const string WalletSetup = "/wallet/setup";
    public const string WalletSend = "/wallet/send";
    public const string WalletReceive = "/wallet/receive";
    public const string Lightning = "/lightning";
    public const string LightningSetup = "/lightning/setup";
    public const string Invoices = "/invoices";
    public const string Invoice = "/invoices/{InvoiceId}";
    public const string PointOfSale = "/pos";
    public const string Logout = "/logout";

    public static string InvoicePath(string invoiceId) => Invoice.Replace("{InvoiceId}", invoiceId);
}
