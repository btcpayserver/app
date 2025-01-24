namespace BTCPayApp.Core;

public class BTCPayAppConfig
{
    public const string Key = "appconfig";
    public string? Passcode { get; set; }
    public string? CurrentStoreId { get; set; }
}
