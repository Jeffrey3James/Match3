using System.Collections.Generic;
using JadedBelles.IAP;
using JadedBelles.Networking;
using Match3Game.Shop;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Data-driven shop UI. Reads the catalog from ShopHandler and drives purchases
/// through JadedBellesIAP so the same panel works for any JadedBelles product
/// (only the ShopHandler slug and prefab styling would change per product).
///
/// SETUP (Inspector drag-and-drop, mirrors LoginPanel conventions):
///   1. Build a Canvas with the shop layout:
///        - GameObject "PanelRoot"            (this component sits here)
///        - Transform  "ItemContainer"        (Vertical or Grid Layout Group; card prefab spawns here)
///        - GameObject "ShopItemCard" prefab  (drag from Project — must have ShopItemCard component)
///        - 3 Buttons for tabs: Bundles, Gems, Boosters
///        - Optional: TextMeshProUGUI status label, close button
///        - Optional: 3 TextMeshProUGUI labels for the wallet HUD (gems, coins, lives)
///   2. Put this component on PanelRoot.
///   3. Drag every field into the matching slot below.
///
/// This panel wires its own button listeners in Awake — no OnClick assignments in the Inspector.
/// </summary>
public class ShopPanel : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Parent Transform where item cards are spawned. Should have a Vertical or Grid Layout Group.")]
    [SerializeField] private Transform itemContainer;
    [Tooltip("Prefab with a ShopItemCard component on the root.")]
    [SerializeField] private GameObject itemCardPrefab;

    [Header("Tab Buttons")]
    [SerializeField] private Button bundlesTab;
    [SerializeField] private Button gemsTab;
    [SerializeField] private Button boostersTab;

    [Header("Optional Chrome")]
    [Tooltip("Optional. Shown while the shop is loading and after purchase failures.")]
    [SerializeField] private TextMeshProUGUI statusText;
    [Tooltip("Optional close button (hides the panel).")]
    [SerializeField] private Button closeButton;
    [Tooltip("Optional. App Store requires a visible Restore button for non-consumables.")]
    [SerializeField] private Button restoreButton;

    [Header("Wallet HUD (optional)")]
    [SerializeField] private TextMeshProUGUI gemsLabel;
    [SerializeField] private TextMeshProUGUI coinsLabel;
    [SerializeField] private TextMeshProUGUI livesLabel;

    private readonly List<ShopItemCard> spawnedCards = new List<ShopItemCard>();
    private string currentTab = ShopItemData.TabBundles;

    // --------------------------------------------------------------
    // Lifecycle
    // --------------------------------------------------------------
    private void Awake()
    {
        if (bundlesTab   != null) bundlesTab.onClick.AddListener(() => SwitchTab(ShopItemData.TabBundles));
        if (gemsTab      != null) gemsTab.onClick.AddListener(()    => SwitchTab(ShopItemData.TabGems));
        if (boostersTab  != null) boostersTab.onClick.AddListener(()=> SwitchTab(ShopItemData.TabBoosters));
        if (closeButton  != null) closeButton.onClick.AddListener(OnCloseClicked);
        if (restoreButton != null) restoreButton.onClick.AddListener(OnRestoreClicked);
    }

    private void OnEnable()
    {
        SetStatus("Loading shop...");

        // Catalog readiness: subscribe if not ready, render immediately if it is.
        if (ShopHandler.instance != null)
        {
            if (ShopHandler.instance.ShopReady)
            {
                RenderCurrentTab();
            }
            else
            {
                ShopHandler.instance.OnShopReady += HandleShopReady;
            }
        }

        // IAP callbacks.
        if (JadedBellesIAP.Instance != null)
        {
            JadedBellesIAP.Instance.OnWalletChanged  += HandleWalletChanged;
            JadedBellesIAP.Instance.OnPurchaseGranted += HandlePurchaseGranted;
            JadedBellesIAP.Instance.OnPurchaseFailed  += HandlePurchaseFailed;
            JadedBellesIAP.Instance.OnShopUnavailable += HandleShopUnavailable;

            // Reflect the last-known wallet immediately, and refresh from the server.
            if (JadedBellesIAP.Instance.Wallet != null) HandleWalletChanged(JadedBellesIAP.Instance.Wallet);
            JadedBellesIAP.Instance.RefreshWallet();
        }
    }

    private void OnDisable()
    {
        if (ShopHandler.instance != null)
        {
            ShopHandler.instance.OnShopReady -= HandleShopReady;
        }
        if (JadedBellesIAP.Instance != null)
        {
            JadedBellesIAP.Instance.OnWalletChanged  -= HandleWalletChanged;
            JadedBellesIAP.Instance.OnPurchaseGranted -= HandlePurchaseGranted;
            JadedBellesIAP.Instance.OnPurchaseFailed  -= HandlePurchaseFailed;
            JadedBellesIAP.Instance.OnShopUnavailable -= HandleShopUnavailable;
        }
    }

    // --------------------------------------------------------------
    // Tabs + rendering
    // --------------------------------------------------------------
    private void SwitchTab(string tab)
    {
        currentTab = tab;
        RenderCurrentTab();
    }

    private void RenderCurrentTab()
    {
        if (itemContainer == null || itemCardPrefab == null || ShopHandler.instance == null) return;

        ClearSpawned();

        var items = ShopHandler.instance.GetItemsForTab(currentTab);
        if (items.Count == 0)
        {
            SetStatus("Nothing here yet — check back soon.");
            return;
        }
        SetStatus(string.Empty);

        foreach (var item in items)
        {
            var go = Instantiate(itemCardPrefab, itemContainer);
            var card = go.GetComponent<ShopItemCard>();
            if (card == null)
            {
                Debug.LogError("[ShopPanel] itemCardPrefab is missing a ShopItemCard component.");
                Destroy(go);
                continue;
            }
            card.Bind(item);
            spawnedCards.Add(card);
        }
    }

    private void ClearSpawned()
    {
        foreach (var card in spawnedCards)
        {
            if (card != null) Destroy(card.gameObject);
        }
        spawnedCards.Clear();
    }

    // --------------------------------------------------------------
    // Event handlers
    // --------------------------------------------------------------
    private void HandleShopReady()
    {
        if (ShopHandler.instance != null) ShopHandler.instance.OnShopReady -= HandleShopReady;
        RenderCurrentTab();
    }

    private void HandleWalletChanged(WalletData wallet)
    {
        if (wallet == null) return;
        if (gemsLabel  != null) gemsLabel.text  = wallet.gems.ToString();
        if (coinsLabel != null) coinsLabel.text = wallet.coins.ToString();
        if (livesLabel != null) livesLabel.text = wallet.lives.ToString();
    }

    private void HandlePurchaseGranted(PurchaseResult result)
    {
        SetStatus("Purchase complete. Thanks for supporting Xandria.");
        ReenableAllCards();
    }

    private void HandlePurchaseFailed(string message)
    {
        SetStatus(string.IsNullOrEmpty(message) ? "Purchase failed." : message);
        ReenableAllCards();
    }

    private void HandleShopUnavailable()
    {
        SetStatus("The shop is temporarily unavailable. Try again in a moment.");
    }

    private void ReenableAllCards()
    {
        foreach (var card in spawnedCards)
        {
            if (card != null) card.SetBusy(false);
        }
    }

    // --------------------------------------------------------------
    // Buttons
    // --------------------------------------------------------------
    private void OnCloseClicked()
    {
        gameObject.SetActive(false);
    }

    private void OnRestoreClicked()
    {
        if (JadedBellesIAP.Instance == null) return;
        SetStatus("Restoring purchases...");
        JadedBellesIAP.Instance.RestorePurchases();
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message ?? string.Empty;
    }
}
