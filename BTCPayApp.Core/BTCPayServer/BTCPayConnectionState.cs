namespace BTCPayApp.Core.BTCPayServer;

public enum BTCPayConnectionState
{
    Init,
    Disconnected,
    WaitingForAuth,
    Connecting,
    Syncing,
    WaitingForEncryptionKey,
    ConnectedAsMaster,
    ConnectedAsSlave,
    ConnectedFinishedInitialSync
}