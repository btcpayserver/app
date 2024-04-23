using System.ComponentModel.DataAnnotations;

namespace BTCPayApp.Core.Data;

public class Setting
{
    [Key]
    public string Key { get; set; }
    public byte[] Value { get; set; }
    public long Version { get; set; } = 0;


}