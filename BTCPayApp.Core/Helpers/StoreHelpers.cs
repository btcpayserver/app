using System.Text.Json;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.LDK;
using BTCPayApp.Core.Wallet;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;

namespace BTCPayApp.Core.Helpers;

public static class StoreHelpers
{
     public static async Task<(GenericPaymentMethodData? onchain, GenericPaymentMethodData? lightning)?> TryApplyingAppPaymentMethodsToCurrentStore(
         this IAccountManager accountManager,
         OnChainWalletManager onChainWalletManager, LightningNodeManager lightningNodeService, bool applyOnchain, bool applyLighting)
    {
        var storeId = accountManager.GetCurrentStore()?.Id;
        var config = await onChainWalletManager.GetConfig();
        if (// is a store present?
            string.IsNullOrEmpty(storeId) ||
            // is user permitted? (store owner)
            !await accountManager.IsAuthorized(Policies.CanModifyStoreSettings, storeId) ||
            // is the onchain wallet configured?
            !onChainWalletManager.IsConfigured(config)) return null;
        // check the store's payment methods
        var pms = await accountManager.GetClient().GetStorePaymentMethods(storeId, includeConfig: true);
        // onchain
        var onchain = pms.FirstOrDefault(pm => pm.PaymentMethodId == OnChainWalletManager.PaymentMethodId);
        if (applyOnchain && config?.Derivations.TryGetValue(WalletDerivation.NativeSegwit, out var derivation) is true && onchain is null)
        {
                onchain = await accountManager.GetClient().UpdateStorePaymentMethod(storeId, OnChainWalletManager.PaymentMethodId, new UpdatePaymentMethodRequest
                {
                    Enabled = true,
                    Config = derivation.Descriptor
                });
        }

        // lightning
        var lightning = pms.FirstOrDefault(pm => pm.PaymentMethodId == LightningNodeManager.PaymentMethodId);
        if (applyLighting && lightning is null && lightningNodeService.IsActive)
        {
            var key = await lightningNodeService.Node.ApiKeyManager.Create("Automated BTCPay Store Setup",
                APIKeyPermission.Write);
            lightning = await accountManager.GetClient().UpdateStorePaymentMethod(storeId,
                LightningNodeManager.PaymentMethodId, new UpdatePaymentMethodRequest
                {
                    Enabled = true,
                    Config = key.ConnectionString(accountManager.GetUserInfo().UserId)
                });
        }

        return (onchain, lightning);
    }


    public static async Task<bool> IsOnChainOurs(this OnChainWalletManager onChainWalletManager, GenericPaymentMethodData? onchain)
    {
        if (!string.IsNullOrEmpty(onchain?.Config.ToString()))
        {
            var config = await onChainWalletManager.GetConfig();
            using var jsonDoc = JsonDocument.Parse(onchain.Config.ToString());
            if (jsonDoc.RootElement.TryGetProperty("accountDerivation", out var derivationSchemeElement) &&
                derivationSchemeElement.GetString() is { } derivationScheme && 
                config.Derivations.Any(pair => pair.Value.Identifier == 
                                               $"DERIVATIONSCHEME:{derivationScheme}"))
            {
                return true;
            }
        }

        return false;
    }
    
    public static async Task<bool> IsLightningOurs(this LightningNodeManager lightningNodeManager, GenericPaymentMethodData? lightning)
    {
        if (!string.IsNullOrEmpty(lightning?.Config.ToString()))
        {
            using var jsonDoc = JsonDocument.Parse(lightning.Config.ToString());
            if (jsonDoc.RootElement.TryGetProperty("connectionString", out var connectionStringElement) &&
                connectionStringElement.GetString() is { } connectionString && 
                LightningConnectionStringHelper.ExtractValues(connectionString, out var lnConnectionString) is { } lnValues && 
                lnConnectionString == "app" && 
                lnValues.TryGetValue("key", out var key) &&
                key is not null &&
                await lightningNodeManager.Node.ApiKeyManager.CheckPermission(key, APIKeyPermission.Read))
            {
                return true;
            }
        }

        return false;
    }
}