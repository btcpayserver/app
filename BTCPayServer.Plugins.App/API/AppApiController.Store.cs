using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayApp.Core.Models;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.App.API;

public partial class AppApiController
{
    [HttpGet("create-store")]
    [Authorize(Policy = Policies.CanModifyStoreSettingsUnscoped, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> CreateStore()
    {
        var defaultTemplate = await storeRepository.GetDefaultStoreTemplate();
        var blob = defaultTemplate.GetStoreBlob();
        var defaultCurrency = blob.DefaultCurrency;
        var defaultExchange = defaultRules.GetRecommendedExchange(defaultCurrency);
        var exchanges = rateFactory.RateProviderFactory
            .AvailableRateProviders
            .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(k => k.Id!, k => k.DisplayName);
        var recommendation = exchanges.First(e => e.Key == defaultExchange);
        var preferredExchangeId = recommendation.Key;

        return Ok(new CreateStoreData
        {
            Name = defaultTemplate.StoreName,
            DefaultCurrency = defaultCurrency,
            Exchanges = exchanges,
            PreferredExchangeId = preferredExchangeId,
            CanEditPreferredExchange = blob.GetRateSettings(false)?.RateScripting is not true,
            CanAutoCreate = !string.IsNullOrEmpty(defaultTemplate.StoreName) && !string.IsNullOrEmpty(preferredExchangeId)
        });
    }
}
