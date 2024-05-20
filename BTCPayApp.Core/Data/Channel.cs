namespace BTCPayApp.Core.Data;

public class Channel
{
    public string Id { get; set; }
    public List<string> Aliases { get; set; }
    public byte[] Data { get; set; }

    public string? FundingScript { get; set; }
    
    
}
