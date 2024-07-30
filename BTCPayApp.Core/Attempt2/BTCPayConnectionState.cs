namespace BTCPayApp.Core.Attempt2;

public enum BTCPayConnectionState
{
    Init,
    WaitingForAuth,
    Connecting,
    Syncing,
    Disconnected,
    ConnectedAsMaster,
    ConnectedAsSlave,
    ConnectedFinishedInitialSync
}