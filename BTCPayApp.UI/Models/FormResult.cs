namespace BTCPayApp.UI.Models;

public class FormResult(bool succeeded, string[]? errorList = null)
{
    public bool Succeeded { get; set; } = succeeded;
    public string[]? ErrorList { get; set; } = errorList;
    public FormResult(string[] errors) : this(false, errors) { }
    public FormResult(string error) : this([error]) { }
}
