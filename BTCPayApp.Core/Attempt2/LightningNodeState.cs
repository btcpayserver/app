namespace BTCPayApp.Core.Attempt2;

public enum LightningNodeState
{
    Init,
    NotConfigured,
    WaitingForConnection,
    Loading,
    Loaded,
    Stopped,
    Error
}
