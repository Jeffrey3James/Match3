using JadedBelles.IAP;
using Match3Game.Shop;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One shop item row. Populated at runtime from ShopItemData.
///
/// SETUP (prefab, all Inspector drag-and-drop):
///   1. Build a UI Panel with:
///        - Image           "Icon"           (item icon sprite)
///        - TextMeshProUGUI "Title"
///        - TextMeshProUGUI "Description"    (optional)
///        - TextMeshProUGUI "Rewards"        (auto-composed from rewards list)
///        - GameObject      "BadgeRoot"      (parent that hides if no badge)
///        - TextMeshProUGUI "BadgeLabel"
///        - Button          "Buy"
///        - TextMeshProUGUI "BuyLabel"       (shows priceLabel)
///   2. Put this component on the row root.
///   3. Drag every field into the matching slot below. No OnClick wiring
///      needed - the component adds its own listener in Bind.
/// </summary>
public class ShopItemCard : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI descriptionLabel;
    [SerializeField] private TextMeshProUGUI rewardsLabel;

    [Header("Badge")]
    [Tooltip("Optional. Hidden when the item has no badge string.")]
    [SerializeField] private GameObject badgeRoot;
    [SerializeField] private TextMeshProUGUI badgeLabel;

    [Header("Buy")]
    [SerializeField] private Button buyButton;
    [SerializeField] private TextMeshProUGUI buyLabel;

    private ShopItemData boundItem;

    public void Bind(ShopItemData item)
    {
        if (item == null) return;
        boundItem = item;

        if (titleLabel != null) titleLabel.text = item.title;
        if (descriptionLabel != null) descriptionLabel.text = item.description ?? string.Empty;
        if (rewardsLabel != null) rewardsLabel.text = ComposeRewardsSummary(item);
        if (buyLabel != null) buyLabel.text = item.priceLabel;

        if (icon != null && !string.IsNullOrEmpty(item.iconSprite))
        {
            // Sprites live in Resources/UI/XandriaKit/ — mirrors LevelHandler's bundled-asset pattern.
            var sprite = Resources.Load<Sprite>("UI/XandriaKit/" + item.iconSprite);
            if (sprite != null) icon.sourceImage = sprite;
        }

        bool hasBadge = !string.IsNullOrEmpty(item.badge);
        if (badgeRoot != null) badgeRoot.SetActive(hasBadge);
        if (hasBadge && badgeLabel != null) badgeLabel.text = item.badge;

        if (buyButton != null)
        {
            buyButton.onClick.RemoveAllListeners();
            buyButton.onClick.AddListener(OnBuyClicked);
        }
    }

    /// <summary>Toggle the buy button while a purchase is in flight for this item.</summary>
    public void SetBusy(bool busy)
    {
        if (buyButton != null) buyButton.interactable = !busy;
    }

    /// <summary>Bound item id, or empty when nothing is bound yet.</summary>
    public string ItemId => boundItem?.id ?? string.Empty;

    private void OnBuyClicked()
    {
        if (boundItem == null || JadedBellesIAP.Instance == null) return;
        SetBusy(true);
        JadedBellesIAP.Instance.BuyItem(boundItem.id);
        // ShopPanel re-enables the button when OnPurchaseGranted / OnPurchaseFailed fires.
    }

    private static string ComposeRewardsSummary(ShopItemData item)
    {
        if (item.rewards == null || item.rewards.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < item.rewards.Count; i++)
        {
            var r = item.rewards[i];
            if (r == null) continue;
            if (sb.Length > 0) sb.Append("   ");
            switch ((r.type ?? string.Empty).ToLowerInvariant())
            {
                case ShopRewardData.TypeGems:    sb.Append(r.amount).Append(" gems"); break;
                case ShopRewardData.TypeCoins:   sb.Append(r.amount).Append(" coins"); break;
                case ShopRewardData.TypeLives:   sb.Append(r.amount).Append(" lives"); break;
                case ShopRewardData.TypeBooster: sb.Append(r.amount).Append("x ").Append(r.boosterId); break;
                default:                         sb.Append(r.amount).Append(" ").Append(r.type); break;
            }
        }
        return sb.ToString();
    }
}
