namespace BTCPayApp.UI.Models;

public class Notification
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public DateTimeOffset Created { get; set; }
    public string? Body { get; set; }
    public bool Seen { get; set; }
}
