using System.Text.Json.Serialization;

namespace BTCPayApp.Core.BTCPayServer;

[JsonConverter(typeof(JsonStringEnumConverter))]
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
