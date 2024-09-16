using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BTCPayApp.UI.Models;

public class AppItemModel
{
    [Required]
    public string? Title { get; set; }
    [Required]
    public string? Id {  get; set; }
    [Required]
    public string? PriceType { get; set; }
    public decimal? Price {  get; set; }
    public string? Categories { get; set; }
    public int? Inventory { get; set; }
    public string? Description { get; set; }
    [JsonPropertyName("image")]
    public string? ImageUrl { get; set; }
    public string? BuyButtonText { get; set; }
    public bool Disabled { get; set; }
    [JsonIgnore]
    public bool Enabled
    {
        get => !Disabled;
        set => Disabled = !value;
    }
}
