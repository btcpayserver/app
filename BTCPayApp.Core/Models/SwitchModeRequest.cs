namespace BTCPayApp.Core.Models;

public class SwitchModeRequest
{
    public required string StoreId { get; init; }
    public required string Mode { get; init; }
}
