using BTCPayServer.Client.Models;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayApp.Core.Models;

public class CreatePosInvoiceRequest
{
    public string? AppId { get; set; }
    public List<AppCartItem>? Cart { get; set; }
    public int? DiscountPercent { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? DiscountAmount { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? Tip { get; set; }
    public string? PosData { get; set; }
}