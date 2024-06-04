namespace BTCPayApp.UI.Features;

public record RemoteData<T>(T? Data, bool Loading = false, string? Error = null)
{
    public T? Data = Data;
    public bool Loading = Loading;
    public string? Error = Error;
}
