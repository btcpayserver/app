using System.Text.Json.Serialization;

namespace BTCPayApp.Core.LDK;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum APIKeyPermission
{
    Read,
    Write,
}
