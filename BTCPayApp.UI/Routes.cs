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
    public const string Error = "/error";
    public const string NotFound = "/not-found";

    // authorized
    public const string Dashboard = "/dashboard";
    public const string Notifications = "/notifications";
    public const string EnterPasscode = "/passcode";
    public const string Withdraw = "/withdraw";
    public const string Invoices = "/invoices";
    public const string Invoice = "/invoices/{InvoiceId}";
    public const string Checkout = "/checkout/{InvoiceId}";
    public const string PointOfSale = "/pos";
    public const string Logout = "/logout";
    public const string CreateStore = "/store/create";
    public const string Settings = "/settings";
    public const string ChangePasscode = "/settings/passcode";
    public const string AppLogs = "/settings/applogs";
    public const string SelectStore = "/settings/select-store";
    public const string Store = "/settings/store/{StoreId}";
    public const string StoreUsers = "/settings/store/{StoreId}/users";
    public const string StoreUser = "/settings/store/{StoreId}/users/{UserId}";
    public const string InviteStoreUser = "/settings/store/{StoreId}/invite-user";
    public const string PosSettings = "/settings/pos/{AppId}";
    public const string NotificationSettings = "/settings/notifications";
    public const string WalletSettings = "/settings/wallet";
    public const string WalletSeed = "/settings/wallet/seed";
    public const string WalletSeedConfirm = "/settings/wallet/seed-confirmation";
    public const string WalletFunds = "/settings/wallet/funds";
    public const string LightningSettings = "/settings/lightning";
    public const string ChannelsPeers = "/settings/lightning/channels";
    public const string Account = "/settings/account";
    public const string ChangePassword = "/settings/password";
    public const string Pairing = "/settings/pairing";
    public const string LightningSend = "/lightning/send";
    public const string LightningReceive = "/lightning/receive";
    public const string ServerUsers = "/settings/server/users";
    public const string ServerUser = "/settings/server/users/{UserId}";
    public const string InviteServerUser = "/settings/server/invite-user";

    // paths with params
    public static string StorePath(string storeId) => Store.Replace("{StoreId}", storeId);
    public static string StoreUsersPath(string storeId) => StoreUsers.Replace("{StoreId}", storeId);
    public static string StoreUserPath(string storeId, string userId) => StoreUser.Replace("{StoreId}", storeId).Replace("{UserId}", userId);
    public static string InviteStoreUserPath(string storeId) => InviteStoreUser.Replace("{StoreId}", storeId);
    public static string InvoicePath(string invoiceId) => Invoice.Replace("{InvoiceId}", invoiceId);
    public static string CheckoutPath(string invoiceId) => Checkout.Replace("{InvoiceId}", invoiceId);
    public static string PosSettingsPath(string appId) => PosSettings.Replace("{AppId}", appId);
    public static string ServerUserPath(string userId) => ServerUser.Replace("{UserId}", userId);
}
