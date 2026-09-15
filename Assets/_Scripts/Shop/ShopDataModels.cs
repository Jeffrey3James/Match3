using System;
using System.Collections.Generic;

namespace Match3Game.Shop
{
    /// <summary>
    /// Plain serializable models for the data-driven shop pipeline.
    /// Authored in Assets/Resources/Shop/shop.json and served remotely by the
    /// JadedBelles API at /api/v1/match3/shop. Parsed with JsonUtility, so every
    /// type here must stay a simple [Serializable] class (no nested arrays, no dictionaries).
    /// </summary>
    [Serializable]
    public class ShopItemData
    {
        /// <summary>Stable string id, e.g. "gem_pack_small" or "bundle_starter".</summary>
        public string id;

        /// <summary>Which tab to show under. "bundles", "gems", or "boosters".</summary>
        public string tab;

        /// <summary>Display title, e.g. "Starter Pack".</summary>
        public string title;

        /// <summary>Optional short blurb shown under the title.</summary>
        public string description;

        /// <summary>Asset name of the sprite in Resources/UI/XandriaKit or a full sprite path.</summary>
        public string iconSprite;

        /// <summary>Optional badge text, e.g. "BEST VALUE" or "-30%". Empty/null = no badge.</summary>
        public string badge;

        /// <summary>Price string as shown to the player, e.g. "$4.99" or "500 gems".</summary>
        public string priceLabel;

        /// <summary>Currency the price is denominated in. "usd", "gems", "coins".</summary>
        public string currency;

        /// <summary>Numeric price used for sorting and analytics.</summary>
        public float priceValue;

        /// <summary>Contents granted on purchase. Multiple entries stack.</summary>
        public List<ShopRewardData> rewards = new List<ShopRewardData>();

        /// <summary>Optional order override. Lower numbers show first. Ties fall back to priceValue.</summary>
        public int sortOrder;

        public const string TabBundles = "bundles";
        public const string TabGems = "gems";
        public const string TabBoosters = "boosters";

        public const string CurrencyUsd = "usd";
        public const string CurrencyGems = "gems";
        public const string CurrencyCoins = "coins";
    }

    [Serializable]
    public class ShopRewardData
    {
        /// <summary>Reward type: "gems", "coins", "lives", "booster".</summary>
        public string type;

        /// <summary>For "booster", the booster asset name (e.g. "Bomb", "Rocket").</summary>
        public string boosterId;

        /// <summary>Amount granted.</summary>
        public int amount;

        public const string TypeGems = "gems";
        public const string TypeCoins = "coins";
        public const string TypeLives = "lives";
        public const string TypeBooster = "booster";
    }

    [Serializable]
    public class ShopCatalog
    {
        public int version;
        public List<ShopItemData> items = new List<ShopItemData>();
    }
}
