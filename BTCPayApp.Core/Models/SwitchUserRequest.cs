namespace BTCPayApp.Core.Models;

public class SwitchUserRequest
{
    public required string StoreId { get; init; }
    public required string UserId { get; init; }
}
