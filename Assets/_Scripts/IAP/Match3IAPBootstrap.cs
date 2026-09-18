using System.Collections;
using JadedBelles.IAP;
using Match3Game.Shop;
using UnityEngine;

/// <summary>
/// Boots the reusable JadedBellesIAP service for the Match3 product. Drop one of
/// these on a scene GameObject that owns JadedBellesIAP. Waits for ShopHandler to
/// finish loading the catalog, then wires the store driver and initializes IAP.
///
/// Any other JadedBelles game copies this file, swaps the slug and driver, and
/// gets the same purchase pipeline. Nothing in JadedBellesIAP or the server
/// verify endpoint changes.
/// </summary>
[RequireComponent(typeof(JadedBellesIAP))]
public class Match3IAPBootstrap : MonoBehaviour
{
    private const string ProductSlug = "match3";

    private JadedBellesIAP iap;

    private void Awake()
    {
        iap = GetComponent<JadedBellesIAP>();
    }

    private IEnumerator Start()
    {
        // Wait for the shop catalog to hydrate. ShopHandler exposes OnShopReady
        // for event-driven wiring; polling is fine because the wait is short.
        while (ShopHandler.instance == null || !ShopHandler.instance.ShopReady)
        {
            yield return null;
        }

        IStorePurchaseDriver driver;
#if UNITY_PURCHASING
        driver = new UnityIAPPurchaseDriver();
#else
        // No Unity IAP package installed. Use the editor fake so shop UI still functions.
        Debug.LogWarning("[Match3IAPBootstrap] UNITY_PURCHASING is not defined. Using EditorFakePurchaseDriver.");
        driver = new EditorFakePurchaseDriver();
#endif

        iap.Initialize(ProductSlug, ShopHandler.instance.GetAllItems(), driver);
    }
}
