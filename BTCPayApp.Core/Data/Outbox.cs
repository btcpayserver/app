namespace BTCPayApp.Core.Data;

public class Outbox
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public OutboxAction ActionType { get; set; }
    public required string Key { get; set; }
    public required string Entity { get; set; }
    public required long Version { get; set; }
}
