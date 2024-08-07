﻿namespace BTCPayApp.UI;

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
    public const string Loading = "/loading";
    public const string NotFound = "/not-found";

    // authorized
    public const string Dashboard = "/dashboard";
    public const string Notifications = "/notifications";
    public const string EnterPasscode = "/passcode";
    public const string Pair = "/pair";
    public const string Wallet = "/wallet";
    public const string WalletSend = "/wallet/send";
    public const string WalletReceive = "/wallet/receive";
    public const string Lightning = "/lightning";
    public const string Invoices = "/invoices";
    public const string Invoice = "/invoices/{InvoiceId}";
    public const string PointOfSale = "/pos";
    public const string Logout = "/logout";
    public const string CreateStore = "/store/create";
    public const string Settings = "/settings";
    public const string ChangePasscode = "/settings/passcode";
    public const string SelectStore = "/settings/select-store";
    public const string Store = "/settings/store/{StoreId}";
    public const string PosSettings = "/settings/pos/{AppId}";
    public const string NotificationSettings = "/settings/notifications";
    public const string Withdraw = "/settings/withdraw";
    public const string WalletSettings = "/settings/wallet";
    public const string LightningSettings = "/settings/lightning";
    public const string ChannelsPeers = "/settings/lightning/channels";
    public const string User = "/settings/user";
    public const string EncryptionKey = "/settings/encryption";
    public static string InvoicePath(string invoiceId) => Invoice.Replace("{InvoiceId}", invoiceId);
    public static string StorePath(string storeId) => Store.Replace("{StoreId}", storeId);
    public static string PosSettingsPath(string appId) => PosSettings.Replace("{AppId}", appId);
}
