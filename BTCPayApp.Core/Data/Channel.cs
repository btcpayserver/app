namespace BTCPayApp.Core.Data;

public class Channel:VersionedData
{
    public string Id { get; set; }
    public List<string> Aliases { get; set; }
    public byte[] Data { get; set; }


    public override string Entity => "Channel";
}
