using System;
using System.Collections.Generic;
using JadedBelles.Networking;
using Match3Game.Shop;
using UnityEngine;

namespace JadedBelles.IAP
{
    /// <summary>
    /// Reusable microtransaction workflow for any JadedBelles product.
    ///
    /// Flow (client-side):
    ///   1. Game boots  -> Initialize(slug, catalog, driver)
    ///   2. Player taps -> BuyItem(itemId)
    ///   3. Store confirms         (driver.Purchase onSuccess)
    ///   4. Send receipt to server (JadedBellesApiClient.VerifyPurchase)
    ///   5. Server grants rewards, returns updated wallet
    ///   6. driver.FinishTransaction consumes the transaction with the store
    ///   7. OnPurchaseGranted fires with the granted rewards + fresh wallet
    ///
    /// The client NEVER grants rewards locally. The server response is the only
    /// source of truth, which means:
    ///   - refunds and chargebacks reconcile automatically on next wallet fetch
    ///   - reinstalls restore balances without local save files
    ///   - the same code path works for iOS, Android, and web (Stripe) purchases
    ///
    /// This class is intentionally product-agnostic. Match3 is the first caller;
    /// Jaded Dead and the Booking App instantiate it with their own slug + driver.
    /// </summary>
    public class JadedBellesIAP : MonoBehaviour
    {
        public static JadedBellesIAP Instance { get; private set; }

        /// <summary>True when the store is initialized and the wallet has been fetched.</summary>
        public bool IsReady { get; private set; }

        /// <summary>The player's current wallet (may be null before the first fetch).</summary>
        public WalletData Wallet { get; private set; }

        /// <summary>Fires whenever the wallet balance changes (after grant or explicit refresh).</summary>
        public event Action<WalletData> OnWalletChanged;

        /// <summary>Fires after a purchase is verified and granted by the server.</summary>
        public event Action<PurchaseResult> OnPurchaseGranted;

        /// <summary>Fires when a purchase fails at any step (store, network, verification).</summary>
        public event Action<string> OnPurchaseFailed;

        /// <summary>Fires when the store or the shop catalog is not yet ready to sell.</summary>
        public event Action OnShopUnavailable;

        private string productSlug;
        private IStorePurchaseDriver driver;
        private readonly Dictionary<string, ShopItemData> catalogById = new Dictionary<string, ShopItemData>();
        // Per-item guard so a double-tap on Buy doesn't fire two store transactions.
        private readonly HashSet<string> inFlightItems = new HashSet<string>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Wire the service to one JadedBelles product. Call once at boot after the
        /// player is authenticated. slug is the product identifier the API knows
        /// (e.g. "match3", "jaded-dead"); catalog is the loaded ShopCatalog items;
        /// driver is the platform store implementation.
        /// </summary>
        public void Initialize(string slug, List<ShopItemData> catalog, IStorePurchaseDriver driver)
        {
            if (string.IsNullOrEmpty(slug))
            {
                Debug.LogError("[JadedBellesIAP] Initialize called with an empty slug.");
                OnShopUnavailable?.Invoke();
                return;
            }
            if (catalog == null || catalog.Count == 0)
            {
                Debug.LogWarning("[JadedBellesIAP] Catalog is empty. Shop will be unavailable.");
                OnShopUnavailable?.Invoke();
                return;
            }
            if (driver == null)
            {
                Debug.LogError("[JadedBellesIAP] Store driver is null.");
                OnShopUnavailable?.Invoke();
                return;
            }

            productSlug = slug;
            this.driver = driver;
            catalogById.Clear();
            var storeSkus = new List<string>();
            foreach (var item in catalog)
            {
                if (item == null || string.IsNullOrEmpty(item.id)) continue;
                catalogById[item.id] = item;
                // Only real-money items go to the store. Gem/coin priced items
                // are handled entirely by the server via a different endpoint.
                if (string.Equals(item.currency, ShopItemData.CurrencyUsd, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(item.id))
                {
                    storeSkus.Add(item.id);
                }
            }

            driver.Initialize(
                storeSkus.ToArray(),
                onReady: () => RefreshWallet(_ => IsReady = true),
                onError: err =>
                {
                    Debug.LogError($"[JadedBellesIAP] Store init failed: {err}");
                    OnShopUnavailable?.Invoke();
                });
        }

        /// <summary>
        /// Pull the latest wallet from the server. Call after login, on shop open,
        /// and after any grant to reconcile refunds/chargebacks the server processed
        /// out of band.
        /// </summary>
        public void RefreshWallet(Action<WalletData> onDone = null)
        {
            if (string.IsNullOrEmpty(productSlug)) { onDone?.Invoke(null); return; }

            JadedBellesApiClient.Instance.GetWallet(productSlug,
                response =>
                {
                    if (response != null && response.success && response.data != null)
                    {
                        Wallet = response.data;
                        OnWalletChanged?.Invoke(Wallet);
                    }
                    onDone?.Invoke(Wallet);
                },
                error =>
                {
                    Debug.LogWarning($"[JadedBellesIAP] Wallet fetch failed: {error}");
                    onDone?.Invoke(Wallet);
                });
        }

        /// <summary>
        /// Buy a catalog item. Routes real-money items through the store driver + server
        /// verification, and virtual-currency items through the server directly.
        /// </summary>
        public void BuyItem(string itemId)
        {
            if (!catalogById.TryGetValue(itemId, out var item))
            {
                OnPurchaseFailed?.Invoke($"Item '{itemId}' is not in the catalog.");
                return;
            }
            if (inFlightItems.Contains(itemId))
            {
                Debug.Log($"[JadedBellesIAP] Ignoring duplicate purchase tap for {itemId}.");
                return;
            }
            if (string.Equals(item.currency, ShopItemData.CurrencyUsd, StringComparison.OrdinalIgnoreCase))
            {
                BuyRealMoney(item);
            }
            else
            {
                BuyWithVirtualCurrency(item);
            }
        }

        private void BuyRealMoney(ShopItemData item)
        {
            if (driver == null || !driver.IsReady)
            {
                OnPurchaseFailed?.Invoke("Store is not ready yet. Try again in a moment.");
                return;
            }

            inFlightItems.Add(item.id);
            driver.Purchase(item.id,
                onSuccess: receipt => VerifyWithServer(item, receipt),
                onCancel: () =>
                {
                    inFlightItems.Remove(item.id);
                    // User-cancel is not a failure worth alerting on; log only.
                    Debug.Log($"[JadedBellesIAP] User cancelled purchase of {item.id}.");
                },
                onError: err =>
                {
                    inFlightItems.Remove(item.id);
                    OnPurchaseFailed?.Invoke($"Store error: {err}");
                });
        }

        private void BuyWithVirtualCurrency(ShopItemData item)
        {
            // Virtual-currency purchases post the itemId with no store receipt.
            // Server debits the wallet, then grants. Same endpoint, empty receipt fields.
            inFlightItems.Add(item.id);
            var body = new PurchaseVerifyRequest
            {
                itemId = item.id,
                platform = item.currency, // "gems" | "coins"
                productId = item.id,
                transactionId = Guid.NewGuid().ToString("N"),
                receipt = string.Empty,
                clientNonce = Guid.NewGuid().ToString("N"),
            };
            PostVerify(item, body, storeReceipt: null);
        }

        private void VerifyWithServer(ShopItemData item, StorePurchaseReceipt receipt)
        {
            var body = new PurchaseVerifyRequest
            {
                itemId = item.id,
                platform = receipt.platform,
                productId = receipt.productId,
                transactionId = receipt.transactionId,
                receipt = receipt.receipt,
                clientNonce = Guid.NewGuid().ToString("N"),
            };
            PostVerify(item, body, receipt);
        }

        private void PostVerify(ShopItemData item, PurchaseVerifyRequest body, StorePurchaseReceipt storeReceipt)
        {
            JadedBellesApiClient.Instance.VerifyPurchase(productSlug, body,
                response =>
                {
                    inFlightItems.Remove(item.id);
                    if (response == null || !response.success || response.data == null)
                    {
                        var msg = response?.message ?? "Verification failed.";
                        OnPurchaseFailed?.Invoke(msg);
                        // Do NOT finish the store transaction on failure — leaves it
                        // pending so the next boot can retry with the same receipt.
                        return;
                    }

                    // Consume the store transaction only after the server confirmed the grant.
                    if (storeReceipt != null && driver != null)
                    {
                        driver.FinishTransaction(storeReceipt);
                    }

                    if (response.data.wallet != null)
                    {
                        Wallet = response.data.wallet;
                        OnWalletChanged?.Invoke(Wallet);
                    }
                    OnPurchaseGranted?.Invoke(response.data);
                },
                error =>
                {
                    inFlightItems.Remove(item.id);
                    OnPurchaseFailed?.Invoke($"Verify failed: {error}");
                    // Same as above: leave the store transaction pending for a retry on next boot.
                });
        }

        /// <summary>
        /// Restore non-consumable purchases (subscriptions, remove-ads, etc). App Store
        /// requires a user-visible Restore button. Each restored receipt is re-verified
        /// with the server so a fresh install re-hydrates entitlements.
        /// </summary>
        public void RestorePurchases()
        {
            if (driver == null || !driver.IsReady)
            {
                OnPurchaseFailed?.Invoke("Store is not ready yet.");
                return;
            }

            driver.RestorePurchases(
                onRestored: receipt =>
                {
                    if (!catalogById.TryGetValue(receipt.productId, out var item))
                    {
                        Debug.LogWarning($"[JadedBellesIAP] Restore received unknown product {receipt.productId}.");
                        return;
                    }
                    VerifyWithServer(item, receipt);
                },
                onError: err => OnPurchaseFailed?.Invoke($"Restore failed: {err}"));
        }
    }
}
