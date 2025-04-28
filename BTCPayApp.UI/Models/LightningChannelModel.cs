using BTCPayServer.Lightning;

namespace BTCPayApp.UI.Models;

public class LightningChannelModel
{
    public string Id { get; set; } = null!;
    public byte[]? ChannelId { get; set; }
    public string[]? AlternateIds { get; set; }
    public string? CounterpartyNodeId { get; set; }
    public string? CounterpartyLabel { get; set; }
    public bool Connected { get; set; }
    public bool Active { get; set; }
    public bool? Usable { get; set; }
    public bool? Ready { get; set; }
    public bool? Announced { get; set; }
    public string? State { get; set; }
    public int? Confirmations { get; set; }
    public int? ConfirmationsRequired { get; set; }
    public string? FundingTxHash { get; set; }
    public LightMoney? CapacityOutbound { get; set; }
    public LightMoney? CapacityInbound { get; set; }
}
