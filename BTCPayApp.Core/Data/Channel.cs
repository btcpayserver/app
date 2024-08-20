using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTCPayApp.Core.Data;

public class Channel:VersionedData
{
    public string Id { get; set; }
    public byte[] Data { get; set; }
    public List<ChannelAlias> Aliases { get; set; }
    public long Checkpoint { get; set; }
    public bool Archived { get; set; }
    
    

    [JsonExtensionData] public Dictionary<string, JsonElement> AdditionalData { get; set; } = new();

    public override string EntityKey
    {
        get => $"Channel_{Id}";
        init { }
    }
}

public class ChannelAlias
{
    public string Id { get; set; }
    public string Type { get; set; }
    public string ChannelId { get; set; }
    [JsonIgnore]
    public Channel Channel { get; set; }
}


