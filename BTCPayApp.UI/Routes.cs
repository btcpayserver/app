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
    public const string Settings = "/settings";
    public const string Pair = "/pair";
    public const string WalletSetup = "/wallet/setup";
    public const string PointOfSale = "/pos";
    public const string Logout = "/logout";
}
