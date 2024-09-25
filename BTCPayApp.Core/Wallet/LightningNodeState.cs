namespace BTCPayApp.Core.Wallet;

public enum LightningNodeState
{
    Init,
    NotConfigured,
    WaitingForConnection,
    Loading,
    Loaded,
    Stopped,
    Error,
    Inactive
}
