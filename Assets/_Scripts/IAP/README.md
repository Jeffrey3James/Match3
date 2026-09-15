# JadedBelles IAP — Reusable Microtransaction Workflow

One code path for every JadedBelles game's real-money purchases.
Match3 is the first caller; Jaded Dead, the Booking App, and future products drop
these files in unchanged and only swap the product slug + platform driver.

## Design principles

1. **Server owns the wallet.** The client never grants rewards locally. Refunds,
   chargebacks, and reinstalls reconcile automatically on next wallet fetch.
2. **Data-driven catalog.** Prices, rewards, badges, and layout live in
   `shop.json` (bundled fallback) served remotely by the API — same pattern as
   `levels.json`. Ship a new bundle without a client build.
3. **Store-neutral verify endpoint.** One route accepts iOS, Android, and Stripe
   receipts. Adding a store means adding a driver, not an endpoint.
4. **Idempotent grants.** Every purchase includes a client-generated `clientNonce`
   so a retry after a network flake returns the original grant, not a duplicate.

## Files

```
Assets/_Scripts/
  Shop/ShopDataModels.cs                — ShopItemData, ShopRewardData, ShopCatalog
  Managers/ShopHandler.cs               — Remote-first catalog loader (mirrors LevelHandler)
  Resources/Shop/shop.json              — Bundled fallback catalog
  Networking/ApiModels.IAP.cs           — Request/response contracts
  Networking/JadedBellesApiClient.cs    — GetShopCatalog / GetWallet / VerifyPurchase
  IAP/IStorePurchaseDriver.cs           — Store-agnostic driver interface
  IAP/JadedBellesIAP.cs                 — Reusable service — no platform code
  IAP/UnityIAPPurchaseDriver.cs         — iOS + Android via Unity IAP package
  IAP/EditorFakePurchaseDriver.cs       — In-editor fake for pipeline testing
  IAP/Match3IAPBootstrap.cs             — Match3's boot wiring (copy per product)
```

## Client flow

```
Player taps Buy
      |
      v
JadedBellesIAP.BuyItem(itemId)
      |
      v
IStorePurchaseDriver.Purchase(sku)   -->  Apple / Google / Stripe
      |
      v  StorePurchaseReceipt
      |
      v
POST /api/v1/games/{slug}/purchases/verify
      |
      v  ApiResponsePurchase (granted rewards + fresh wallet)
      |
      +---> driver.FinishTransaction  (consume the store transaction)
      +---> OnWalletChanged
      +---> OnPurchaseGranted
```

If the client crashes between store-confirm and server-verify, the transaction is
still `Pending` at the store. On next boot Unity IAP replays it through
`ProcessPurchase`, JadedBellesIAP re-verifies with the server, the server sees the
same `transactionId` and returns `already_granted` — no double grant.

## Server contract (endpoints the Central API needs)

### `GET /api/v1/games/{slug}/shop`

Anonymous. Returns raw `ShopCatalog` JSON — same shape as
`Assets/Resources/Shop/shop.json`.

### `GET /api/v1/games/{slug}/wallet`

Auth required. Returns `ApiResponseWallet` for the current user + product.

### `POST /api/v1/games/{slug}/purchases/verify`

Auth required. Body:

```json
{
  "itemId":        "bundle_starter",
  "platform":      "apple" | "google" | "stripe" | "gems" | "coins",
  "productId":     "com.jadedbelles.match3.bundle_starter",
  "transactionId": "1000000123456789",
  "receipt":       "<base64 receipt / purchase token / payment_intent id>",
  "clientNonce":   "a1b2c3d4..."
}
```

Server steps:

1. Look up `Purchase` by `(userId, productSlug, transactionId)`. If found, return
   the stored result (idempotent replay).
2. Verify the receipt with the matching store:
   - `apple`  — POST to https://buy.itunes.apple.com/verifyReceipt
   - `google` — Google Play Developer API `purchases.products.get`
   - `stripe` — retrieve the PaymentIntent and check `status == succeeded`
   - `gems` / `coins` — no store call; debit the wallet by `item.priceValue`
3. Look up the shop item by `itemId`. Reject if `productId` does not match the
   item's platform SKU.
4. In a single transaction: insert a `Purchase` row, credit the rewards to the
   player's wallet, and return the updated wallet.
5. Store the response keyed by `clientNonce` for 24 hours so a client retry
   returns the exact same body.

Return shape:

```json
{
  "success": true,
  "message": "",
  "data": {
    "purchaseId":   "pur_...",
    "status":       "granted" | "already_granted" | "refunded" | "pending" | "invalid",
    "granted": [
      { "type": "gems",    "boosterId": "",       "amount": 100 },
      { "type": "coins",   "boosterId": "",       "amount": 500 },
      { "type": "booster", "boosterId": "Bomb",   "amount": 1   }
    ],
    "wallet": {
      "gems": 100, "coins": 500, "lives": 5,
      "boosters": [ { "boosterId": "Bomb", "amount": 1 } ]
    }
  }
}
```

### Database

Two tables per player, shared across products via slug:

```
Purchases(
  Id             uniqueidentifier PK,
  UserId         uniqueidentifier FK -> Users,
  ProductSlug    nvarchar(64),
  ItemId         nvarchar(128),
  Platform       nvarchar(16),
  StoreProductId nvarchar(256),
  TransactionId  nvarchar(256),
  ClientNonce    nvarchar(64),
  Status         nvarchar(24),
  PriceValue     decimal(10,2),
  Currency       nvarchar(8),
  ReceiptRaw     nvarchar(max),
  CreatedUtc     datetime2,
  UNIQUE (UserId, ProductSlug, TransactionId)
)

Wallets(
  UserId         uniqueidentifier,
  ProductSlug    nvarchar(64),
  Gems           int,
  Coins          int,
  Lives          int,
  BoostersJson   nvarchar(max),  -- serialized List<BoosterBalanceData>
  UpdatedUtc     datetime2,
  PRIMARY KEY (UserId, ProductSlug)
)
```

## Adding a new JadedBelles game

1. Copy `Match3IAPBootstrap.cs`, rename, and change `ProductSlug`.
2. Add a `shop.json` to the new product's Resources, or the API's `App_Data`.
3. Drop `JadedBellesIAP` + the new bootstrap on a scene GameObject.
4. Hook shop UI Buy buttons to `JadedBellesIAP.Instance.BuyItem(item.id)`.
5. Register the product's SKUs in App Store Connect / Play Console / Stripe.

Nothing in `JadedBellesIAP`, the driver, or the server verify route changes.
