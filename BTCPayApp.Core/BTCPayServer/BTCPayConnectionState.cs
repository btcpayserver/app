namespace BTCPayApp.Core.BTCPayServer;

public enum BTCPayConnectionState
{
    Init,
    Disconnected,
    WaitingForAuth,
    Connecting,
    Syncing,
    WaitingForEncryptionKey,
    ConnectedAsPrimary,
    ConnectedAsSecondary,
    ConnectedFinishedInitialSync
}
