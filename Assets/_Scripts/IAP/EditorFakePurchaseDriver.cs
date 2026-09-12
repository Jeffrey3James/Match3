using System;
using System.Collections.Generic;
using UnityEngine;

namespace JadedBelles.IAP
{
    /// <summary>
    /// In-Editor driver that fakes store responses so the full purchase pipeline
    /// (shop UI -> driver -> server verify -> wallet update) can be exercised
    /// without Unity IAP. Ships in dev builds only; auto-selected when
    /// UNITY_PURCHASING is not defined. Always returns success immediately.
    /// </summary>
    public class EditorFakePurchaseDriver : IStorePurchaseDriver
    {
        public bool IsReady { get; private set; }

        private HashSet<string> knownProducts = new HashSet<string>();

        public void Initialize(string[] productIds, Action onReady, Action<string> onError)
        {
            knownProducts = new HashSet<string>(productIds ?? Array.Empty<string>());
            IsReady = true;
            onReady?.Invoke();
        }

        public void Purchase(string productId, Action<StorePurchaseReceipt> onSuccess,
            Action onCancel, Action<string> onError)
        {
            if (!knownProducts.Contains(productId))
            {
                onError?.Invoke($"Fake store: unknown product {productId}.");
                return;
            }
            Debug.Log($"[EditorFakePurchaseDriver] Simulated purchase of {productId}.");
            onSuccess?.Invoke(new StorePurchaseReceipt
            {
                platform = "editor",
                productId = productId,
                transactionId = Guid.NewGuid().ToString("N"),
                receipt = "{\"fake\":true}",
            });
        }

        public void FinishTransaction(StorePurchaseReceipt receipt) { /* no-op */ }

        public void RestorePurchases(Action<StorePurchaseReceipt> onRestored, Action<string> onError)
        {
            // Nothing to restore in the fake store.
            onError?.Invoke("Editor fake store has nothing to restore.");
        }
    }
}
