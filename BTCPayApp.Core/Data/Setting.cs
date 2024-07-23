using System.ComponentModel.DataAnnotations;

namespace BTCPayApp.Core.Data;

public class Setting:VersionedData
{
    [Key]
    public string Key { get; set; }
    public byte[] Value { get; set; }
    public bool Backup { get; set; } = true;
}
