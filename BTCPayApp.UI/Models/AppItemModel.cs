using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BTCPayApp.UI.Util;
using BTCPayServer.Client.Models;

namespace BTCPayApp.UI.Models;

public class AppItemModel(AppItem item)
{
    private AppItem Item { get; } = item;

    [Required]
    public string? Title { get => Item.Title; set => Item.Title = value; }

    [Required]
    public string? Id { get => Item.Id; set => Item.Id = value; }

    [Required]
    public AppItemPriceType PriceType { get => Item.PriceType; set => Item.PriceType = value; }

    public decimal? Price { get => Item.Price; set => Item.Price = value; }

    public int? Inventory { get => Item.Inventory; set => Item.Inventory = value; }
    public string? Description { get => Item.Description; set => Item.Description = value; }
    public string? BuyButtonText { get => Item.BuyButtonText; set => Item.BuyButtonText = value; }

    [Url]
    [JsonPropertyName("image")]
    public string? ImageUrl { get => Item.Image; set => Item.Image = value; }

    [JsonIgnore]
    public string? ImagePath { get; set; }

    [JsonIgnore]
    public bool Enabled { get => !Item.Disabled; set => Item.Disabled = !value; }

    public string? Categories
    {
        get => string.Join(',', Item.Categories?? []);
        set => Item.Categories = value != null ? AppItemParser.SplitStringList(',', value) : null;
    }
}
