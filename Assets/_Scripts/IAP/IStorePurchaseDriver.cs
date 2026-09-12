using System;

namespace JadedBelles.IAP
{
    /// <summary>
    /// Store-agnostic purchase driver. Implement one per platform.
    /// The shipped implementation is UnityIAPPurchaseDriver (Unity IAP for iOS + Android).
    /// A future StripeCheckoutDriver will implement this same interface for web builds
    /// so JadedBellesIAP itself never changes.
    /// </summary>
    public interface IStorePurchaseDriver
    {
        /// <summary>
        /// Initialize the store with the SKUs this catalog needs. Called once on startup.
        /// onReady fires when the store is ready to purchase. onError fires if init fails
        /// permanently (the shop UI should degrade to "temporarily unavailable").
        /// </summary>
        void Initialize(string[] productIds, Action onReady, Action<string> onError);

        /// <summary>True after Initialize completed successfully.</summary>
        bool IsReady { get; }

        /// <summary>
        /// Kick off a purchase for a store SKU. onSuccess fires when the store confirms;
        /// the caller then posts the receipt to JadedBelles for verification and grant.
        /// onCancel fires if the user backs out. onError fires for anything else.
        /// </summary>
        void Purchase(string productId, Action<StorePurchaseReceipt> onSuccess,
            Action onCancel, Action<string> onError);

        /// <summary>
        /// Mark a completed purchase as consumed with the store. Call this ONLY after the
        /// JadedBelles server verified the receipt and granted the rewards. Skipping this
        /// step is why players see "duplicate purchase" and lost consumables.
        /// </summary>
        void FinishTransaction(StorePurchaseReceipt receipt);

        /// <summary>
        /// Restore non-consumable purchases (App Store requires a visible "Restore" button).
        /// Fires onRestored once per restored transaction. Consumables are not restored.
        /// </summary>
        void RestorePurchases(Action<StorePurchaseReceipt> onRestored, Action<string> onError);
    }

    /// <summary>Store-issued receipt handed off to the server for verification.</summary>
    public class StorePurchaseReceipt
    {
        public string platform;      // "apple" | "google" | "stripe"
        public string productId;
        public string transactionId;
        public string receipt;       // raw payload for the server
    }
}
