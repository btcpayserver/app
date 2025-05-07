using System.Text.Json.Serialization;

namespace BTCPayApp.Core.Data;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutboxAction
{
    Insert,
    Update,
    Delete
}
