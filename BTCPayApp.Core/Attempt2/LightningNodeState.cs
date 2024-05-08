namespace BTCPayApp.Core.Data;

public enum LightningNodeState
{
    Init,
    NodeNotConfigured,
    WaitingForConnection,
    Loading,
    Loaded,
    // Stopped,
    Error
}
