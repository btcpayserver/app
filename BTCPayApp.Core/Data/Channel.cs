namespace BTCPayApp.Core.Data;

public class Channel:VersionedData
{
    public string Id { get; set; }
    public byte[] Data { get; set; }
    public List<ChannelAlias> Aliases { get; set; }
}

public class ChannelAlias
{
    public string Id { get; set; }
    public string Type { get; set; }
    public string ChannelId { get; set; }
    public Channel Channel { get; set; }
}
