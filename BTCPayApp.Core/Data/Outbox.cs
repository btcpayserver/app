namespace BTCPayApp.Core.Data;

public class Outbox
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public OutboxAction ActionType { get; set; }
    public string Key { get; set; }
    public string Entity { get; set; }
    public long Version { get; set; }
}