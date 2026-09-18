using System;
using System.Collections.Generic;

namespace JadedBelles.Networking
{
    // ---------- Requests ----------

    /// <summary>
    /// Body for POST /api/v1/games/{slug}/purchases/verify.
    /// Sent after the store (Apple/Google/Stripe) confirms the transaction.
    /// The server re-verifies with the store, then grants the rewards.
    /// The client MUST NOT grant rewards locally — the server response is the
    /// only source of truth. This shape is intentionally store-neutral so the
    /// same endpoint handles iOS, Android, and Stripe web purchases.
    /// </summary>
    [Serializable]
    public class PurchaseVerifyRequest
    {
        /// <summary>Shop item id from the catalog (e.g. "bundle_starter").</summary>
        public string itemId;

        /// <summary>"apple", "google", or "stripe".</summary>
        public string platform;

        /// <summary>Store-issued product id (App Store SKU / Play SKU / Stripe price id).</summary>
        public string productId;

        /// <summary>Store transaction identifier. Server uses this for idempotency.</summary>
        public string transactionId;

        /// <summary>Raw receipt / purchase token. Apple base64, Google purchaseToken, Stripe payment_intent id.</summary>
        public string receipt;

        /// <summary>
        /// Client-generated GUID sent with the request. Server stores it so a retry
        /// of the same verify call after a network flake does not double-grant.
        /// </summary>
        public string clientNonce;
    }

    // ---------- Responses ----------

    /// <summary>Response from POST /api/v1/games/{slug}/purchases/verify.</summary>
    [Serializable]
    public class ApiResponsePurchase
    {
        public bool success;
        public string message;
        public PurchaseResult data;
    }

    [Serializable]
    public class PurchaseResult
    {
        /// <summary>Server-side purchase id (durable, for support lookups).</summary>
        public string purchaseId;

        /// <summary>"granted", "already_granted", "refunded", "pending", "invalid".</summary>
        public string status;

        /// <summary>Rewards the server actually granted for this purchase.</summary>
        public List<GrantedRewardData> granted = new List<GrantedRewardData>();

        /// <summary>The player's wallet AFTER the grant, so the client can reconcile without a second call.</summary>
        public WalletData wallet;
    }

    [Serializable]
    public class GrantedRewardData
    {
        public string type;       // "gems" | "coins" | "lives" | "booster"
        public string boosterId;  // populated when type == "booster"
        public int amount;
    }

    // ---------- Wallet ----------

    /// <summary>Response for GET /api/v1/games/{slug}/wallet.</summary>
    [Serializable]
    public class ApiResponseWallet
    {
        public bool success;
        public string message;
        public WalletData data;
    }

    /// <summary>
    /// Server-owned wallet balances for one player in one game.
    /// Kept flat so JsonUtility handles it cleanly. Boosters live in a parallel
    /// list because JsonUtility can't serialize a Dictionary.
    /// </summary>
    [Serializable]
    public class WalletData
    {
        public int gems;
        public int coins;
        public int lives;
        public List<BoosterBalanceData> boosters = new List<BoosterBalanceData>();
    }

    [Serializable]
    public class BoosterBalanceData
    {
        public string boosterId;
        public int amount;
    }
}
