using System.Text.Json.Serialization;

namespace BTCPayApp.Core.Wallet;

[JsonConverter(typeof(JsonStringEnumConverter))]
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
