namespace BTCPayApp.Core.Attempt2;

public enum BTCPayConnectionState
{
    Init,
    WaitingForAuth,
    Connecting,
    Syncing,
    WaitingForEncryptionKey,
    Disconnected,
    ConnectedAsMaster,
    ConnectedAsSlave,
    ConnectedFinishedInitialSync
}