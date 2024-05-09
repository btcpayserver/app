namespace BTCPayApp.Core.Attempt2;

public enum LightningNodeState
{
    Init,
    NodeNotConfigured,
    WaitingForConnection,
    Loading,
    Loaded,
    Stopped,
    Error
}
