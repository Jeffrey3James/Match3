# Xandria Gem Jam — Ad Monetization Plan

**Owner:** Game / Unity engineering  
**Platform scope:** Android and iOS first; WebGL remains ad-free for this release  
**Primary SDK:** Unity LevelPlay (formerly ironSource)

## Executive summary

Implement a **rewarded-first**, player-choice monetization model. Start with “Watch ad → +5 moves” at the precise moment a player runs out of moves; then add an interstitial only at natural breaks and a rewarded continue after failure. Do **not** add banner ads.

Use **Unity LevelPlay** as the single mediation layer. It is the best fit for a Unity 6 Android/iOS game because it can mediate demand from AdMob, Meta Audience Network, Unity Ads, and AppLovin through one Unity integration and one reporting/control surface. Google AdMob alone is a reasonable alternative only for an Android-only title that values the fewest moving parts over mediation competition and cross-network yield.

The older repo was investigated and contains no ad, analytics, optimization, remote-config, extra-moves, continue, or life-system implementation worth migrating. See [`INVESTIGATION-old-match3.md`](INVESTIGATION-old-match3.md). Implement against the current project’s existing game events and persistence system instead.

---

## 1. Current project integration map

The plan deliberately targets existing Xandria Gem Jam seams rather than inventing a parallel game loop:

| Need | Current integration point | Implementation action |
|---|---|---|
| Detect loss / show recovery offer | `Assets/_Scripts/GameEvents.cs:62-67` emits `onLevelFailed`; `Assets/_Scripts/UI/Match3UI.cs:87-91` shows the game-over window. | Add a “Continue: watch ad (+5 moves)” card/button to the loss UI and subscribe once to `onLevelFailed`. Do not auto-open a rewarded ad. |
| Add moves after a rewarded ad | `Assets/_Scripts/Match3.cs:762-767` decrements moves; failure locks input at `775-789`. | Add a small public `GrantExtraMoves(int amount)` / `ResumeAfterRewardedContinue(int amount)` API in `Match3` that increases `movesLeft`, updates text, clears the terminal lock, and re-enables input. Do not reload the scene. |
| Detect a successful level | `GameEvents.cs:55-60` emits `onLevelCompleted`; `Match3.cs:94-129` finalizes score then invokes `ScoreFinalized`. | Log level completion immediately; offer Double Coins only after the completion/reward panel is visible; schedule an interstitial only after the player exits/advances. |
| Reward coins and save | `PlayerHandler.AddCoins` is at `Assets/_Scripts/Managers/PlayerHandler.cs:104-110`; `PlayerDataManager.UpdatePlayerData` persists local-first at `Assets/_Scripts/Utils/PlayerDataManager.cs:119-169`. | Credit base coins exactly once on win, then a rewarded Double Coins offer credits the same base amount exactly once and calls `UpdatePlayerData()`. Add an idempotency flag per completed level. |
| Power-up reward | Existing power-up types include Bomb, Rocket, Hammer, Missile, and Nuke in `Assets/_Scripts/GemTypes/PowerUpGems.cs:4-12`. | Keep a pending “next level starter power-up” in player/session state. On level boot, convert it to a spawned/available power-up using a purpose-built board API; do not try to alter private board state from UI. |
| Lives | Five lives and a 20-minute configured timer already exist in `Assets/_Scripts/Managers/PlayerHandler.cs:16-18,132-170`; new players start with five at `PlayerDataManager.cs:56-60`. | Treat a rewarded +1 life as a later, optional placement only after fixing capacity and regeneration. `AddALifeToPlayer()` currently has an inverted cap test at `PlayerHandler.cs:167-170`, and no scheduled regeneration was found. |
| First-three-level suppression | Player progression is `PlayerHandler.playerData.playerLevel` (`PlayerHandler.cs:36-39`). | Convert this zero-based index to a displayed level number before evaluating the rule; suppress all ad offers for displayed levels 1–3. |

### Existing service state

`Packages/manifest.json` contains no LevelPlay/Mediation/AdMob package. Unity Connect, Unity Analytics, and Unity Ads are disabled and have no game IDs in `ProjectSettings/UnityConnectSettings.asset:7-34`. The project is Unity `6000.0.34f1`, has a Unity cloud project ID, and currently has an Android build profile; the README states WebGL and iOS profiles still need creation.

---

## 2. SDK decision: Unity LevelPlay

### Recommendation

Adopt **Unity LevelPlay** as the only runtime ad SDK/mediation layer for mobile. Configure one rewarded placement and one interstitial placement first, then enable selected network adapters in the LevelPlay dashboard.

**Why it wins here**

- Unity-native integration is the least disruptive path for this Unity 6 game.
- Mediation lets the game compete demand from AdMob, Meta, Unity Ads, and AppLovin instead of committing to one buyer/network.
- Rewarded and interstitial formats, placement reporting, frequency controls, and consent-related setup live behind one vendor integration.
- It keeps Android and iOS on the same abstraction while allowing the game to remain **ad-free on WebGL**, where mobile ad support is limited and not worth a divergent implementation.

**Alternative — Google AdMob:** choose it only if the product becomes Android-only and the priority is the smallest possible initial setup rather than mediated yield and multi-network competition. It is not the recommendation for the stated Android + iOS + WebGL roadmap.

### Setup sources

Before implementation, follow the current official SDK/package and privacy documentation rather than pinning a version from this document:

- Unity LevelPlay documentation: <https://docs.unity.com/grow/levelplay/>
- Unity LevelPlay Unity SDK documentation: <https://docs.unity.com/grow/levelplay/mediation/unity-plugin>
- Unity LevelPlay privacy and consent documentation: <https://docs.unity.com/grow/levelplay/mediation/privacy>
- Apple App Tracking Transparency guidance: <https://developer.apple.com/documentation/apptrackingtransparency>
- Google EU User Consent Policy: <https://www.google.com/about/company/user-consent-policy/>

### Package manifest change

Add the LevelPlay package recommended by the current Unity documentation to `Packages/manifest.json`; resolve packages in Unity, then commit the generated `packages-lock.json` when it appears. The expected direct dependency is:

```json
{
  "dependencies": {
    "com.unity.services.levelplay": "<version approved in the current LevelPlay documentation>"
  }
}
```

If the installed/current official guide for the selected Unity 6 release directs the project to the mediation package instead, use this **instead of**, not alongside, the line above:

```json
"com.unity.services.mediation": "<version approved in the current Unity documentation>"
```

Do not guess a package version; use the Unity Package Manager/official guide so the package matches Unity 6. Enter Android/iOS app keys, test device IDs, and real placement IDs in LevelPlay’s dashboard. Use test ads in development and real ads only in production builds.

---

## 3. Placement design (value exchange first)

### 3.1 Rewarded: Extra Moves — launch placement

- **Moment:** the player is out of moves but has not completed objectives.
- **Offer:** “Watch ad → +5 moves.”
- **Value:** lets a near-win player finish the board they already understand; this is typically the strongest match-3 rewarded opportunity.
- **Cap:** maximum **2 rewarded extra-move grants per level attempt**.
- **UI:** explicit primary action and a visible **“No thanks”** action. If no ad is ready, leave the player on the fail UI—never promise a reward or substitute an interstitial.
- **Implementation:** preserve the board, grant 5 moves, set `isGameOver = false`, re-enable `inputReader`, and invoke the same move-text update used by normal play. This requires a small new public board API because `movesLeft` and `isGameOver` are private today.

### 3.2 Rewarded: Continue after fail

- **Moment:** the level-fail panel is displayed after an unsuccessful attempt.
- **Offer:** “Watch ad → keep this board + 5 moves.”
- **Value:** avoids throwing away an engaged player’s board progress.
- **Cap:** **one per level attempt**. This is mutually exclusive with the first extra-moves recovery offer; do not show both labels for the same rescue.
- **Rollout:** Phase 2, after the Phase 1 state-resume path is stable.

### 3.3 Rewarded: Free power-up before level

- **Moment:** pre-level/start panel, before the board is instantiated.
- **Offer:** “Watch ad → start with 1 Bomb” (or Rocket/Nuke after tuning).
- **Value:** a voluntary tactical advantage, not a gate.
- **Constraint:** one per level start; grant it through a durable “pending starter power-up” state and consume it exactly once during board setup.

### 3.4 Rewarded: Double coins

- **Moment:** after a level is completed and the base reward is already shown.
- **Offer:** “Watch ad → 2× coins.”
- **Value:** transparent bonus to a reward the player has already earned.
- **Rule:** credit base coins once, and credit one additional equal amount after the reward callback. Never recalculate score or replay completion code.

### 3.5 Interstitial: between levels

- **Moment:** after every **third level completion**, at the natural transition after the results screen / before the next board.
- **Value/UX rationale:** it appears only at a clear break, never mid-board or before a player can see their completion reward.
- **Additional gate:** 30-second placement cooldown, global interstitial cooldown (below), no recent rewarded ad, no ads for levels 1–3, and disabled for a future paid “remove ads” entitlement.

### 3.6 Interstitial: on repeated fail

- **Moment:** after every **second** level fail, after the player finishes the fail UI decision—not automatically on every failure.
- **Value/UX rationale:** this is a lower-priority monetization opportunity; it must never feel like punishment for an individual loss.
- **Additional gate:** only if the player did not just watch/accept a rewarded offer and all global caps pass. If a rewarded rescue is available, prioritize it and do not show an interstitial.

### Explicit non-placement: no banner ads

**Do not use banner ads.** Persistent banners distract from the board, reduce puzzle readability, commonly hurt retention, and tend to produce less useful revenue per engaged match-3 player than well-timed rewarded placements. No banner container or banner SDK work should be added.

---

## 4. Frequency caps and UX guardrails

These are release requirements, not tuning suggestions:

1. **First three displayed levels:** zero ads of every format, including reward offers.
2. **Global interstitial cooldown:** at least **90 seconds** between any two interstitial impressions. Maintain this across scene loads and app pause/resume in persistent state.
3. **Placement-level interstitial cooldown:** also require at least 30 seconds for the selected placement; the stricter rule always wins.
4. **No back-to-back formats:** never show an interstitial immediately before or after a rewarded ad. Require the player to return to gameplay or a menu/reward transition before another ad can be eligible.
5. **Rewarded is opt-in:** every offer must state the exact benefit and include a clear **No thanks** route. A close/dismiss/no-fill/error grants nothing and does not punish the player.
6. **No surprise ads:** never interrupt an active swap, cascade, power-up, tutorial, or animation. Interstitials only at menus/transitions after state is saved.
7. **Future paid users:** add `hasRemovedAds` to persisted player data/entitlements. When true, disable **all** ad requests and all ad UI (rewarded and interstitial), rather than merely suppressing interstitials.
8. **Safety defaults:** if consent is unavailable, initialization fails, a placement is not ready, the SDK errors, or the app is on WebGL, fail closed: hide the offer and preserve normal game flow.

### Privacy and store compliance

- Implement LevelPlay’s consent/privacy flow before loading/showing ads. Collect and pass GDPR/EEA consent and applicable US privacy signals according to current SDK guidance.
- On iOS, time the ATT request as a contextual pre-prompt after onboarding—not on first frame—and respect denial without reducing core game access.
- Determine whether the game is directed to children / mixed audience before release. Configure COPPA/age-gating and mediation network settings accordingly; do not enable personalized ads where not permitted.
- Maintain privacy policy disclosures, data-safety forms, consent records/signals, and SDK dependency disclosures for both stores. Have legal/privacy review the final setup; this plan is not legal advice.

---

## 5. Economy tuning tied to ads

### Starting values

| System | Recommended launch value | Notes |
|---|---:|---|
| Base completion reward | **25 coins × level reward multiplier** | Start simple: multiplier 1.0 for levels 1–20, then tune from actual completion/coin-sink data. Existing code currently adds `scoreForThisLevel` as coins; separate a named base reward from score before Double Coins. |
| Rewarded Double Coins | **2× base reward** | Grant the difference (an additional base reward) after verified reward completion. |
| Rewarded Extra Moves | **+5 moves per completed rewarded view** | Cap at 2 per level attempt. |
| Continue after fail | **keep board state +5 moves** | Cap at 1 per attempt; mutually exclusive with duplicate fail-rescue offers. |
| Starter power-up | **1 Bomb at launch** | Progress to Rocket/Nuke only after retention and difficulty data supports it. |

### Life system decision

This project already has a five-life model, even though its current implementation needs repair. If the team wants an energy gate, retain it as **5 hearts, 1 heart regenerated per 30 minutes, and an optional rewarded “+1 heart” only when below cap**. If the team does **not** want a life system, remove/hide the lives UI and do not introduce a rewarded life placement merely to create demand.

Before monetizing lives, fix the cap condition at `PlayerHandler.AddALifeToPlayer()` and add/test an actual regeneration loop based on `playerLifeCountdown`; the current source configures 1,200 seconds but does not show a replenishment execution path.

---

## 6. Analytics and optimization event contract

Use LevelPlay reporting for network revenue/impressions and add a lightweight game event wrapper that can initially log structured JSON to the console/back end and later forward to an analytics product. Keep the game wrapper provider-agnostic; it should never block gameplay or ad callbacks.

**Common parameters on every event:** `event_id`, `utc_timestamp`, `session_id`, `anonymous_or_account_id` (pseudonymous), `platform`, `app_version`, `build_number`, `country` if consent/policy allows, `level_id`, `level_attempt`, `player_level`, `ad_free_entitled`, `consent_status`, `source` (organic/known campaign if available).

| Event | When it fires | Required additional parameters |
|---|---|---|
| `app_session_start` | Session begins after consent decision | `first_session`, `consent_flow_shown`, `ads_initialized` |
| `level_start` | Board becomes playable | `max_moves`, `lives_before`, `starter_powerup`, `entry_source` |
| `level_complete` | Objectives complete, before ad decisions | `moves_left`, `base_coin_reward`, `duration_seconds`, `attempt_number` |
| `level_fail_no_ad` | Player exits/retries a loss without a rewarded rescue | `moves_left`, `fail_count_for_level`, `reason` |
| `level_fail_ad_offered` | A rewarded continue/extra-moves card becomes visible | `placement`, `offer_type`, `offer_count_this_attempt`, `ad_ready` |
| `level_fail_ad_accepted` | Player taps the rewarded call-to-action | `placement`, `offer_type`, `moves_to_grant` |
| `extra_moves_purchased` | Reward verified and moves applied | `placement`, `moves_granted`, `offer_count_this_attempt`, `resume_success` |
| `rewarded_offer_shown` | Any rewarded offer is shown in UI | `placement`, `reward_type`, `reward_amount`, `context` |
| `rewarded_offer_declined` | Player taps No thanks/closes the offer UI | `placement`, `context` |
| `ad_load_result` | SDK load callback returns | `format`, `placement`, `result`, `error_code`, `latency_ms` |
| `ad_show_attempt` | Before requesting a show | `format`, `placement`, `eligible`, `block_reason` |
| `ad_impression` | SDK reports an impression | `format`, `placement`, `network`, `ad_unit_id`, `revenue_usd` if provided, `precision` |
| `ad_reward_granted` | Rewarded completion callback verifies reward | `placement`, `reward_type`, `reward_amount`, `network` |
| `ad_closed_no_reward` | Rewarded unit closes without reward / fails to display | `placement`, `failure_stage`, `error_code` |
| `interstitial_skipped_by_cap` | Eligibility suppresses an otherwise scheduled interstitial | `trigger`, `block_reason`, `seconds_since_last_interstitial` |
| `coins_granted` | Any non-idempotent coin award is persisted | `reason`, `amount`, `balance_after`, `grant_id` |

### Optimization sequence

1. For the first two weeks, optimize only reliability and retention: fill rate, rewarded completion rate, ad error rate, D1/D7 retention, level-fail rate, and time to first ad.
2. Compare cohorts using release/config switches only after enough data; test one variable at a time (for example, +3 versus +5 moves, or third versus fourth completion interstitial cadence).
3. Never use an experiment to remove consent, bypass caps, or force rewarded ads.
4. When remote configuration is introduced, use it only for numeric caps/eligibility/placement enablement. Keep the default values hard-coded locally so an outage cannot make the game hostile.

---

## 7. Implementation architecture

### Persistent manager and adapter boundary

Create `Assets/_Scripts/Monetization/AdManager.cs` on the existing boot scene (`AuthSplashScreen`) next to other persistent singletons. It should survive scene changes, own eligibility/cooldowns, and call an SDK-specific `IAdProvider` adapter. Keep SDK callbacks out of `Match3`, `Match3UI`, and `PlayerHandler`.

**Do not copy the old repo:** it has no ad integration. The current project’s central event hub is the correct place to subscribe to level completion/failure; the UI should request an offer, while `AdManager` remains the authority on readiness and caps.

### C# sketch (provider-agnostic shell)

The adapter below is intentionally small: implement `IAdProvider` with the callbacks/API names from the installed LevelPlay package version, rather than relying on stale sample code. The manager’s public API stays stable if the SDK changes.

```csharp
using System;
using UnityEngine;

public sealed class AdManager : MonoBehaviour
{
    public static AdManager Instance { get; private set; }
    [SerializeField] private bool adsDisabledForThisBuild;
    private IAdProvider provider;
    private float lastInterstitialAt = -999f;

    public bool HasRemoveAdsEntitlement { get; set; }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        provider = new LevelPlayProvider(); // Adapter owns current SDK API details.
    }

    public void Initialize(ConsentState consent)
    {
        if (adsDisabledForThisBuild || HasRemoveAdsEntitlement || Application.platform == RuntimePlatform.WebGLPlayer) return;
        provider.Initialize(consent, Preload);
    }

    public void ShowRewarded(string placementId, Action onReward, Action onFail)
    {
        if (!CanOfferRewarded() || !provider.IsRewardedReady(placementId)) { onFail?.Invoke(); return; }
        AnalyticsEvent.Log("ad_show_attempt", "format", "rewarded", "placement", placementId);
        provider.ShowRewarded(placementId,
            () => { AnalyticsEvent.Log("ad_reward_granted", "placement", placementId); onReward?.Invoke(); Preload(); },
            error => { AnalyticsEvent.Log("ad_closed_no_reward", "placement", placementId, "error", error); onFail?.Invoke(); Preload(); });
    }

    public void ShowInterstitial()
    {
        if (!CanShowInterstitial() || !provider.IsInterstitialReady()) return;
        lastInterstitialAt = Time.unscaledTime;
        provider.ShowInterstitial(() => { AnalyticsEvent.Log("ad_impression", "format", "interstitial"); Preload(); });
    }

    private bool CanOfferRewarded() => !adsDisabledForThisBuild && !HasRemoveAdsEntitlement && PlayerProgress.DisplayedLevel > 3;
    private bool CanShowInterstitial() => CanOfferRewarded() && Time.unscaledTime - lastInterstitialAt >= 90f;
    private void Preload() { provider.LoadRewarded("extra_moves"); provider.LoadInterstitial(); }
}

public interface IAdProvider
{
    void Initialize(ConsentState consent, Action onReady);
    bool IsRewardedReady(string placementId); bool IsInterstitialReady();
    void LoadRewarded(string placementId); void LoadInterstitial();
    void ShowRewarded(string placementId, Action reward, Action<string> noReward);
    void ShowInterstitial(Action impression);
}
```

`ConsentState`, `AnalyticsEvent`, `PlayerProgress`, and `LevelPlayProvider` are deliberate project types to add. `ShowRewarded` must call `onReward` only after LevelPlay’s verified reward callback—not after a button tap or ad close. Replace `Time.unscaledTime` with a persisted/realtime clock if the 90-second interstitial cooldown must survive app restart; for launch, scene-persistent runtime state is sufficient.

### Required gameplay changes for Phase 1

1. In `Match3`, add a guarded public rescue method, for example `TryResumeWithExtraMoves(int amount)`. It must return false if the board is not in a move-exhausted loss state or the amount is invalid.
2. The method should increment `movesLeft`, call `UpdateMovesText()`, set `isGameOver = false`, re-enable `inputReader`, and prevent duplicate grants with an attempt-local reward ID/counter.
3. In `Match3UI`, make the failure panel represent a **decision state**: Retry, Main Menu, and a visible “Watch ad: +5 moves” button only when `AdManager` says the offer is eligible/ready. The No thanks path remains Retry/Main Menu.
4. Persist only durable economy (coins, hearts, remove-ads entitlement, pending power-up). Keep per-attempt rescue cap in memory; reset it when a fresh board starts.
5. Ensure `Match3` does not use level-completion logic to handle a failed-resume board. A rewarded rescue must not re-emit `LevelFailed` or credit coins.

---

## 8. Delivery phases

### Phase 0 — prerequisites and acceptance criteria (about 1–2 hours)

- Create LevelPlay account/project, Android/iOS app entries, placements, test devices, and consent configuration.
- Confirm app package/bundle IDs and store listings. The current README lists `com.JadedBelles.GemJam`.
- Define `hasRemovedAds` as a backward-compatible field in `PlayerData`; default false. Decide whether it is local-only until IAP entitlement sync exists.
- Fix/test the existing life cap/regeneration before exposing any rewarded-life UI.
- Add a mobile-only feature switch (`adsEnabled`) that defaults false outside development/QA configuration.

**Acceptance:** test devices load test ads; WebGL shows no ad UI; a denied/unavailable consent path leaves gameplay intact.

### Phase 1 — MVP: Extra Moves rewarded ad (about 4 hours)

- Install/initialize LevelPlay and a single rewarded placement (`extra_moves`).
- Add `AdManager`, LevelPlay adapter, consent gating, and `ad_load_result` / `ad_reward_granted` logging.
- Add `Match3.TryResumeWithExtraMoves(5)` and one failure-panel offer.
- Enforce levels 1–3 suppression, No thanks, ready-state checks, one reward per callback, and **2 extra-moves views per level attempt**.

**Acceptance:** on a test device, a rewarded completion adds exactly 5 moves and resumes the same board; close/no-fill/error grants zero; a third offer is hidden; retry/menu still work; no ad appears in the first three levels.

### Phase 2 — natural-break interstitial + Continue after fail (about 4 hours)

- Add one interstitial placement (`between_levels`); schedule it after every third completion only after results/transition.
- Add the one-per-attempt Continue offer (`continue_fail`) using the same proven resume API.
- Add fail-count, global 90-second, 30-second placement, no-rewarded-adjacent, and no-ad-free-entitlement gates.
- Instrument all show/skip/error/impression events.

**Acceptance:** no interstitial during board play or immediately around rewarded; cadence and cooldown survive scene changes; paid/ad-free flag suppresses every ad request/UI path.

### Phase 3 — Double Coins, starter power-up, dashboard (estimate after Phase 2 metrics)

- Implement exact-once Double Coins reward with a persisted `grant_id` or per-level reward state.
- Implement one pre-level Bomb placement backed by pending-power-up state.
- Build an analytics dashboard/funnel: ad availability → offer shown → accepted → impression/reward → resumed/completed, with retention and level difficulty segmentation.
- Add remote-config only for safe numeric parameters after local defaults and logging are established.

**Acceptance:** duplicate callback/retry cannot duplicate coins/power-ups; dashboard reconciles game grants to LevelPlay reward/impression counts within expected reporting lag.

---

## 9. QA checklist

- [ ] Android and iOS test ads show with valid test-device configuration; production IDs are not used in development.
- [ ] WebGL build never initializes mediation, never shows a dead reward button, and builds successfully.
- [ ] First three displayed levels show neither rewarded offers nor interstitials.
- [ ] Extra moves grants exactly +5, updates UI, resumes same board, and cannot exceed two views per attempt.
- [ ] Continue is limited to one per attempt and does not stack with an extra-moves rescue.
- [ ] Every rewarded offer has “No thanks”; close/no-fill/failure gives no reward and leaves valid recovery actions.
- [ ] Interstitials obey third-completion / every-second-fail rules, 90-second global cooldown, 30-second placement cooldown, and no rewarded adjacency.
- [ ] A remove-ads entitlement disables all formats and requests.
- [ ] Consent, ATT, COPPA/age configuration, privacy policy, App Store/Play data disclosures, and mediation-network configuration are reviewed before release.
- [ ] Coin, heart, starter-power-up, and remove-ads state survive restart and signed-in sync conflict correctly.

---

## 10. eCPM and revenue realism

Use these as planning ranges—not forecasts; actual results vary substantially by geography, platform, retention, fill, auction demand, ad frequency, and consent rate.

| Format | Indicative US eCPM | Indicative global eCPM |
|---|---:|---:|
| Rewarded video | **$8–$25** | **$2–$8** |
| Interstitial | **$3–$10** | **$1–$3** |

At **1,000 DAU** with moderate engagement (for example, a mix near 1–2 rewarded impressions and roughly 1–2 eligible interstitial impressions per daily active user), a blended, US-weighted small indie match-3 can reasonably plan around **$15–$60/day** before platform/payment considerations. A global-heavy audience or low ad completion/fill will be materially below that range; do not use ads as proof that a low-retention game can be made profitable by increasing frequency.

Track realized revenue per DAU, rewarded completion rate, interstitial impressions per DAU, D1/D7 retention, fail rate, and the percentage of sessions with no ready ad. Scale only placements that preserve retention and player sentiment.

---

## Definition of done for the first release

LevelPlay initializes only after privacy gating on Android/iOS; WebGL is cleanly unsupported; the first three levels are ad-free; a verified rewarded Extra Moves view gives exactly +5 moves on the same board (maximum two per attempt); failures never grant a reward; all ads are optional/appropriately capped; and the required gameplay/ad analytics events are observable end to end. No source changes are made by this document.
