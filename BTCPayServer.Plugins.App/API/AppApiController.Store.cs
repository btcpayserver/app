﻿using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayApp.Core.Models;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.App.API;

public partial class AppApiController
{
    [HttpGet("create-store")]
    [Authorize(Policy = Policies.CanModifyStoreSettingsUnscoped, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> CreateStore()
    {
        var defaultCurrency = (await settingsRepository.GetSettingAsync<PoliciesSettings>())?.DefaultCurrency ?? StoreBlob.StandardDefaultCurrency;
        var defaultExchange = defaultRules.GetRecommendedExchange(defaultCurrency);
        var exchanges = rateFactory.RateProviderFactory
            .AvailableRateProviders
            .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(k => k.Id, k => k.DisplayName);
        var recommendation = exchanges.First(e => e.Key == defaultExchange);

        return Ok(new CreateStoreData
        {
            DefaultCurrency = defaultCurrency,
            Exchanges = exchanges,
            RecommendedExchangeId = recommendation.Key
        });
    }
}
