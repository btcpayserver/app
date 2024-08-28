using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin;

namespace BTCPayApp.UI.Util;

public class DisplayFormatter
{
    public DisplayFormatter()
    {
        _Currencies = LoadData().ToDictionary(k => k.Code, StringComparer.InvariantCultureIgnoreCase);
    }

    public IEnumerable<CurrencyData> Currencies => _Currencies.Values;

    public class CurrencyData
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public int Divisibility { get; set; }
        public string Symbol { get; set; }
        public bool Crypto { get; set; }
    }

    static readonly Dictionary<string, IFormatProvider> _CurrencyProviders = new();

    public enum CurrencyFormat
    {
        Code,
        Symbol,
        CodeAndSymbol,
        None
    }

    public string Currency(decimal value, string currency, CurrencyFormat format = CurrencyFormat.Code)
    {
        var provider = GetNumberFormatInfo(currency, true);
        var currencyData = GetCurrencyData(currency, true);
        var divisibility = currencyData.Divisibility;
        value = value.RoundToSignificant(ref divisibility);
        if (divisibility != provider.CurrencyDecimalDigits)
        {
            provider = (NumberFormatInfo)provider.Clone();
            provider.CurrencyDecimalDigits = divisibility;
        }
        var formatted = value.ToString("C", provider);
        if (currencyData.Code == "SATS") formatted = formatted.Replace(',', ' ');

        if (format == CurrencyFormat.Symbol && currencyData.Code == "BTC")
        {
            format = CurrencyFormat.Code;
        }

        return format switch
        {
            CurrencyFormat.None => formatted.Replace(provider.CurrencySymbol, "").Trim(),
            CurrencyFormat.Code => $"{formatted.Replace(provider.CurrencySymbol, "").Trim()} {currency}",
            CurrencyFormat.Symbol => formatted,
            CurrencyFormat.CodeAndSymbol => $"{formatted} ({currency})",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    public string Currency(Money value, MoneyUnit unit, string currency, CurrencyFormat format = CurrencyFormat.Code)
    {
        return Currency(value.ToDecimal(unit), currency, format);
    }

    public string Currency(string value, string currency, CurrencyFormat format = CurrencyFormat.Code)
    {
        return Currency(decimal.Parse(value, CultureInfo.InvariantCulture), currency, format);
    }

    public decimal Rounded(decimal value, string currency)
    {
        var currencyData = GetCurrencyData(currency, true);
        var divisibility = currencyData.Divisibility;
        return value.RoundToSignificant(ref divisibility);
    }

    private NumberFormatInfo GetNumberFormatInfo(string currency, bool useFallback)
    {
        var data = GetCurrencyProvider(currency);
        if (data is NumberFormatInfo nfi)
            return nfi;
        if (data is CultureInfo ci)
            return ci.NumberFormat;
        if (!useFallback)
            return null;
        return CreateFallbackCurrencyFormatInfo(currency);
    }

    private NumberFormatInfo CreateFallbackCurrencyFormatInfo(string currency)
    {
        var usd = GetNumberFormatInfo("USD", false);
        var currencyInfo = (NumberFormatInfo)usd.Clone();
        currencyInfo.CurrencySymbol = currency;
        return currencyInfo;
    }

    public NumberFormatInfo GetNumberFormatInfo(string currency)
    {
        var curr = GetCurrencyProvider(currency);
        if (curr is CultureInfo cu)
            return cu.NumberFormat;
        if (curr is NumberFormatInfo ni)
            return ni;
        return null;
    }

    private IFormatProvider GetCurrencyProvider(string currency)
    {
        lock (_CurrencyProviders)
        {
            if (_CurrencyProviders.Count == 0)
            {
                foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
                {
                    if (culture switch
                    {
                        { LCID: 0x007F or 0x0000 or 0x0c00 or 0x1000 } => true,
                        { IsNeutralCulture : true } => true,
                        _ => false
                    })
                        continue;
                    try
                    {
                        var symbol = new RegionInfo(culture.LCID).ISOCurrencySymbol;
                        var c = symbol switch
                        {
                            "ARS" or "COP" => ModifyCurrencyDecimalDigit(culture, 0),
                            _ => culture
                        };
                        _CurrencyProviders.TryAdd(symbol, c);
                    }
                    catch { }
                }

                foreach (var curr in _Currencies.Where(pair => pair.Value.Crypto))
                {
                    AddCurrency(_CurrencyProviders, curr.Key, curr.Value.Divisibility, curr.Value.Symbol ?? curr.Value.Code);
                }
            }
            return _CurrencyProviders.TryGet(currency.ToUpperInvariant());
        }
    }

    private CultureInfo ModifyCurrencyDecimalDigit(CultureInfo culture, int decimals)
    {
        var modifiedCulture = new CultureInfo(culture.Name);
        var modifiedNumberFormat = (NumberFormatInfo)modifiedCulture.NumberFormat.Clone();
        modifiedNumberFormat.CurrencyDecimalDigits = decimals;
        modifiedCulture.NumberFormat = modifiedNumberFormat;
        return modifiedCulture;
    }

    private void AddCurrency(Dictionary<string, IFormatProvider> currencyProviders, string code, int divisibility, string symbol)
    {
        var culture = new CultureInfo("en-US");
        var number = new NumberFormatInfo
        {
            CurrencyDecimalDigits = divisibility,
            CurrencySymbol = symbol,
            CurrencyDecimalSeparator = culture.NumberFormat.CurrencyDecimalSeparator,
            CurrencyGroupSeparator = culture.NumberFormat.CurrencyGroupSeparator,
            CurrencyGroupSizes = culture.NumberFormat.CurrencyGroupSizes,
            CurrencyNegativePattern = 8,
            CurrencyPositivePattern = 3,
            NegativeSign = culture.NumberFormat.NegativeSign
        };
        currencyProviders.TryAdd(code, number);
    }

    readonly Dictionary<string, CurrencyData> _Currencies;

    private static CurrencyData[] LoadData()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BTCPayApp.UI.Util.Currencies.json");
        string? content;
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            content = reader.ReadToEnd();
        }
        return JsonSerializer.Deserialize<CurrencyData[]>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }

    private CurrencyData? GetCurrencyData(string currency, bool useFallback)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (!_Currencies.TryGetValue(currency.ToUpperInvariant(), out var result))
        {
            if (useFallback)
            {
                var usd = GetCurrencyData("USD", false)!;
                result = new CurrencyData
                {
                    Code = currency,
                    Crypto = true,
                    Name = currency,
                    Divisibility = usd.Divisibility
                };
            }
        }
        return result;
    }
}
