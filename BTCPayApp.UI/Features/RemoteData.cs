namespace BTCPayApp.UI.Features;

public record RemoteData<T>(T? Data = default, string? Error = null, bool Loading = false, bool Sending = false)
{
    public T? Data = Data;
    public bool Loading = Loading;
    public bool Sending = Sending;
    public string? Error = Error;
}
