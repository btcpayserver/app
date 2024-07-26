using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace BTCPayApp.Core.Data;

public abstract class VersionedData
{
    public long Version { get; set; } = 0;

    public abstract string EntityKey { get; init; }
}
