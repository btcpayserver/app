namespace BTCPayApp.Core.Auth;

public class FormResult(bool succeeded, string[]? messages = null)
{
    public bool Succeeded { get; set; } = succeeded;
    public string[]? Messages { get; set; } = messages;
    public FormResult(bool succeeded, string message) : this(succeeded, [message]) { }
}

public class FormResult<T>(bool succeeded, string message, T? response) : FormResult(succeeded, message)
{
    public T? Response { get; set; } = response;
}
