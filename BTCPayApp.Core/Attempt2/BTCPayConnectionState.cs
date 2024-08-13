namespace BTCPayApp.Core.Attempt2;

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