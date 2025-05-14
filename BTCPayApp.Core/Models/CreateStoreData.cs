namespace BTCPayApp.Core.Models;

public class CreateStoreData
{
    public string? Name { get; set; }
    public string DefaultCurrency { get; set; } = null!;
    public Dictionary<string, string>? Exchanges { get; set; }
    public string? PreferredExchangeId { get; set; }
    public bool CanEditPreferredExchange { get; set; }
    public bool CanAutoCreate { get; set; }
}
