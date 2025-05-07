using System.Text.Json.Serialization;

namespace BTCPayApp.Core.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SetupState
{
    Undetermined,
    Pending,
    Completed,
    Failed
}
