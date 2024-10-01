using System.Text.Json;
using System.Text.RegularExpressions;
using BTCPayApp.UI.Models;
using BTCPayServer.Client.Models;

namespace BTCPayApp.UI.Util;

public class AppItemParser
{
    private const char ListSeparator = ',';

    public static List<AppItem> ConvertItems(object? arr)
    {
        var items = new List<AppItem>();
        var str = arr?.ToString();
        if (!string.IsNullOrEmpty(str))
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(str);
                if (jsonDoc.RootElement is { ValueKind: JsonValueKind.Array } jsonArray)
                {
                    items.AddRange(jsonArray.EnumerateArray()
                        .Select(el => new AppItem
                        {
                            Id = el.TryGetProperty("id", out var id) ? id.GetString() : null,
                            Title = el.TryGetProperty("title", out var title) ? title.GetString() : null,
                            PriceType = el.TryGetProperty("priceType", out var priceType) ? Enum.Parse<AppItemPriceType>(priceType.GetString()!) : AppItemPriceType.Fixed,
                            Price = el.TryGetProperty("price", out var price)
                                ? price.ValueKind == JsonValueKind.String && decimal.TryParse(price.GetString(), out var number) ? number : price.GetDecimal()
                                : null,
                            Inventory = el.TryGetProperty("inventory", out var inventory)
                                ? inventory.ValueKind == JsonValueKind.String && int.TryParse(inventory.GetString(), out var inv) ? inv : price.GetInt32()
                                : null,
                            Categories = el.TryGetProperty("categories", out var categories) ? SplitStringList(ListSeparator, categories.GetString() ?? string.Empty) : null,
                            Description = el.TryGetProperty("description", out var description) ? description.GetString() : null,
                            Image = el.TryGetProperty("image", out var imageUrl) ? imageUrl.GetString() : null,
                            BuyButtonText = el.TryGetProperty("buyButtonText", out var buyButtonText) ? buyButtonText.GetString() : null,
                            Disabled = el.TryGetProperty("disabled", out var disabled) && disabled.GetBoolean()
                        }));
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }
        return items;
    }

    public static string? ItemsToTemplate(AppItem[]? items)
    {
        return items != null
            ? JsonSerializer.Serialize(items, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            })
            : null;
    }

    public static string[] SplitStringList(char separator, string list)
    {
        if (string.IsNullOrEmpty(list)) return [];
        // Remove all characters except numeric and comma
        var charsToDestroy = new Regex(@"[^\d|\" + separator + "]");
        return charsToDestroy.Replace(list, "")
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
    }

    public static int[] SplitStringListToInts(char separator, string list)
    {
        return SplitStringList(separator, list).Select(int.Parse).ToArray();
    }
}
