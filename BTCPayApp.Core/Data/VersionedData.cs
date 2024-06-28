using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayApp.Core.Data;

public abstract class VersionedData
{
    public ulong Version { get; set; } = 0;
    [NotMapped]
    public abstract string Entity { get; }
}