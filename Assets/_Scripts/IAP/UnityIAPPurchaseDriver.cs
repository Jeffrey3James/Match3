#if UNITY_PURCHASING
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace JadedBelles.IAP
{
    /// <summary>
    /// Unity IAP-backed implementation of IStorePurchaseDriver. Handles iOS and Android
    /// through Unity's cross-platform purchasing package. Guarded behind UNITY_PURCHASING
    /// so the project still compiles when the package is not installed (e.g. Editor
    /// without the Services module wired up).
    ///
    /// Install steps:
    ///   1. Package Manager  ->  In-App Purchasing (com.unity.purchasing).
    ///   2. Services window  ->  enable Purchasing for the project.
    ///   3. Player Settings  ->  Scripting Define Symbols  ->  add UNITY_PURCHASING (on iOS + Android).
    /// After that this driver activates automatically; JadedBellesIAP does not change.
    /// </summary>
    public class UnityIAPPurchaseDriver : IStorePurchaseDriver, IDetailedStoreListener
    {
        public bool IsReady { get; private set; }

        private IStoreController controller;
        private IExtensionProvider extensions;
        private Action onReadyCallback;
        private Action<string> onInitError;

        private readonly Dictionary<string, Action<StorePurchaseReceipt>> pendingSuccess =
            new Dictionary<string, Action<StorePurchaseReceipt>>();
        private readonly Dictionary<string, Action> pendingCancel = new Dictionary<string, Action>();
        private readonly Dictionary<string, Action<string>> pendingError = new Dictionary<string, Action<string>>();

        private Action<StorePurchaseReceipt> restoreCallback;
        private Action<string> restoreError;

        public void Initialize(string[] productIds, Action onReady, Action<string> onError)
        {
            onReadyCallback = onReady;
            onInitError = onError;

            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            foreach (var id in productIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                // Consumables by default. Non-consumables (remove-ads, cosmetics) can
                // override via a separate catalog field in a later revision.
                builder.AddProduct(id, ProductType.Consumable);
            }
            UnityPurchasing.Initialize(this, builder);
        }

        public void Purchase(string productId, Action<StorePurchaseReceipt> onSuccess,
            Action onCancel, Action<string> onError)
        {
            if (!IsReady || controller == null)
            {
                onError?.Invoke("Store not initialized.");
                return;
            }
            var product = controller.products.WithID(productId);
            if (product == null || !product.availableToPurchase)
            {
                onError?.Invoke($"Product {productId} is unavailable.");
                return;
            }
            pendingSuccess[productId] = onSuccess;
            pendingCancel[productId] = onCancel;
            pendingError[productId] = onError;
            controller.InitiatePurchase(product);
        }

        public void FinishTransaction(StorePurchaseReceipt receipt)
        {
            if (controller == null || receipt == null) return;
            var product = controller.products.WithID(receipt.productId);
            if (product != null)
            {
                controller.ConfirmPendingPurchase(product);
            }
        }

        public void RestorePurchases(Action<StorePurchaseReceipt> onRestored, Action<string> onError)
        {
            restoreCallback = onRestored;
            restoreError = onError;
#if UNITY_IOS || UNITY_TVOS
            extensions?.GetExtension<IAppleExtensions>()?.RestoreTransactions((ok, err) =>
            {
                if (!ok) onError?.Invoke(err ?? "Restore was declined.");
                // Restored purchases arrive via ProcessPurchase; we route them there.
            });
#else
            // Google auto-restores on init; nothing extra to do.
            onError?.Invoke("Restore is only required on iOS.");
#endif
        }

        // ----- IStoreListener -----

        public void OnInitialized(IStoreController c, IExtensionProvider e)
        {
            controller = c;
            extensions = e;
            IsReady = true;
            onReadyCallback?.Invoke();
        }

        public void OnInitializeFailed(InitializationFailureReason reason) =>
            onInitError?.Invoke($"Init failed: {reason}");

        public void OnInitializeFailed(InitializationFailureReason reason, string message) =>
            onInitError?.Invoke($"Init failed: {reason} ({message})");

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            var receipt = new StorePurchaseReceipt
            {
                platform = DetectPlatform(),
                productId = args.purchasedProduct.definition.id,
                transactionId = args.purchasedProduct.transactionID,
                receipt = args.purchasedProduct.receipt,
            };

            if (pendingSuccess.TryGetValue(receipt.productId, out var success))
            {
                pendingSuccess.Remove(receipt.productId);
                pendingCancel.Remove(receipt.productId);
                pendingError.Remove(receipt.productId);
                success?.Invoke(receipt);
            }
            else
            {
                // Not from a live Buy tap - restored purchase or resumed pending.
                restoreCallback?.Invoke(receipt);
            }

            // Pending: JadedBellesIAP calls FinishTransaction after server-side grant.
            return PurchaseProcessingResult.Pending;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason reason)
        {
            HandleFailure(product?.definition?.id, reason.ToString(), reason == PurchaseFailureReason.UserCancelled);
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription description)
        {
            HandleFailure(product?.definition?.id, description?.message ?? description?.reason.ToString(),
                description?.reason == PurchaseFailureReason.UserCancelled);
        }

        private void HandleFailure(string productId, string message, bool userCancelled)
        {
            if (string.IsNullOrEmpty(productId)) return;
            if (userCancelled && pendingCancel.TryGetValue(productId, out var cancel))
            {
                cancel?.Invoke();
            }
            else if (pendingError.TryGetValue(productId, out var err))
            {
                err?.Invoke(message ?? "Purchase failed.");
            }
            pendingSuccess.Remove(productId);
            pendingCancel.Remove(productId);
            pendingError.Remove(productId);
        }

        private static string DetectPlatform()
        {
#if UNITY_IOS || UNITY_TVOS
            return "apple";
#elif UNITY_ANDROID
            return "google";
#else
            return "editor";
#endif
        }
    }
}
#endif
