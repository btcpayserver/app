namespace BTCPayApp.UI.Models;

public class FormResult(bool succeeded, string[]? messages = null)
{
    public bool Succeeded { get; set; } = succeeded;
    public string[]? Messages { get; set; } = messages;
    public FormResult(bool succeeded, string message) : this(succeeded, [message]) { }
}
