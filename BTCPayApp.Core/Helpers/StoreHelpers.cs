using System.Text.Json;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Auth;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;

namespace BTCPayApp.Core.Helpers;

public static class StoreHelpers
{
     public static async Task<(GenericPaymentMethodData? onchain, GenericPaymentMethodData? lightning)?> TryApplyingAppPaymentMethodsToCurrentStore(
         this IAccountManager accountManager,
         OnChainWalletManager onChainWalletManager, LightningNodeManager lightningNodeService, bool applyOnchain, bool applyLighting)
    {
        var storeId = accountManager.GetCurrentStore()?.Id;
        if (// is a store present?
            string.IsNullOrEmpty(storeId) ||
            // is user permitted? (store owner)
            !await accountManager.IsAuthorized(Policies.CanModifyStoreSettings, storeId) ||
            // is the onchain wallet configured?
            !onChainWalletManager.IsConfigured) return null;
        // check the store's payment methods
        var pms = await accountManager.GetClient().GetStorePaymentMethods(storeId, includeConfig: true);
        // onchain
        var onchain = pms.FirstOrDefault(pm => pm.PaymentMethodId == OnChainWalletManager.PaymentMethodId);
        if (applyOnchain)
        {
            var onchainDerivation = onChainWalletManager.Derivation;
            if (onchain is null && onchainDerivation is not null)
                onchain = await accountManager.GetClient().UpdateStorePaymentMethod(storeId, OnChainWalletManager.PaymentMethodId, new UpdatePaymentMethodRequest
                {
                    Enabled = true,
                    Config = onchainDerivation.Descriptor
                });
            
        }
        // lightning
        var lightning = pms.FirstOrDefault(pm => pm.PaymentMethodId == LightningNodeManager.PaymentMethodId);
        if (applyLighting)
        {
            if (lightning is null && !string.IsNullOrEmpty(lightningNodeService.ConnectionString))
                lightning = await accountManager.GetClient().UpdateStorePaymentMethod(storeId, LightningNodeManager.PaymentMethodId, new UpdatePaymentMethodRequest
                {
                    Enabled = true,
                    Config = lightningNodeService.ConnectionString
                });
            
        }
        return (onchain, lightning);
    }


    public static bool IsOnChainOurs(this OnChainWalletManager onChainWalletManager, GenericPaymentMethodData? onchain)
    {
        if (!string.IsNullOrEmpty(onchain?.Config.ToString()))
        {
            var onchainDerivation = onChainWalletManager.Derivation;
            using var jsonDoc = JsonDocument.Parse(onchain.Config.ToString());
            if (jsonDoc.RootElement.TryGetProperty("accountDerivation", out var derivationSchemeElement) &&
                derivationSchemeElement.GetString() is { } derivationScheme &&
                onchainDerivation?.Identifier.Contains(derivationScheme) is true)
            {
                return true;
            }
        }

        return false;
    }
    
    public static bool IsLightningOurs(this LightningNodeManager lightningNodeManager, GenericPaymentMethodData? lightning)
    {
        if (!string.IsNullOrEmpty(lightning?.Config.ToString()))
        {
            using var jsonDoc = JsonDocument.Parse(lightning.Config.ToString());
            if (jsonDoc.RootElement.TryGetProperty("connectionString", out var connectionStringElement) &&
                connectionStringElement.GetString() is { } connectionString &&
                connectionString == lightningNodeManager.ConnectionString)
            {
                return true;
            }
        }

        return false;
    }
}