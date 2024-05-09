namespace BTCPayApp.Core.Auth;

public class Invoice
{
    public string? Id { get; set; }
    public string? OrderId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset Date { get; set; }
    public string? Currency { get; set; }
    public decimal Amount { get; set; }
}
