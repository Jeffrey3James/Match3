# Ads Setup — Phase 1 (Rewarded "+5 Moves")

The code side is done and merged. The game compiles and runs **ad-free** until you
finish the steps below — everything fails closed (no SDK, no keys, WebGL, or
editor = no ad offers, game plays exactly as before).

## What's in the code

| Piece | File | What it does |
|---|---|---|
| `AdManager` | `Assets/_Scripts/Monetization/AdManager.cs` | Self-bootstrapping singleton. Owns eligibility: no ads on displayed levels 1–3, max 2 extra-move grants per level attempt, WebGL/editor always off. |
| `IAdProvider` / `NullAdProvider` | `Assets/_Scripts/Monetization/IAdProvider.cs` | SDK boundary + fail-closed default. |
| `LevelPlayProvider` | `Assets/_Scripts/Monetization/LevelPlayProvider.cs` | Unity LevelPlay adapter. Compiled only when `LEVELPLAY_ENABLED` is defined. Reward granted only on the SDK's verified `OnAdRewarded` callback (handles the late-reward-after-close case). |
| `Match3.TryResumeWithExtraMoves(int)` | `Assets/_Scripts/Match3.cs` | Un-ends a moves-exhausted board: +moves, clears game-over, re-enables input. Refuses to run on a won board. |
| Fail-panel button | `Assets/_Scripts/UI/Match3UI.cs` | Optional `watchAdExtraMoves` button in the game-over window. Hidden unless an ad is loaded AND the player is eligible. |

Also fixed in this PR: `Match3UI.cs` imported `UnityEditor.PackageManager`
(editor-only — this would have **failed every Android/iOS/WebGL build**).

## Your checklist (in order)

### 1. LevelPlay dashboard (platform.ironsrc.com)
1. Create an account / log in.
2. Add two apps: Android (`com.JadedBelles.XandriaGemAdventure`) and iOS (same bundle id).
   Apps can be added as "not live in store yet" and updated later.
3. For each app, note the **App Key**.
4. Create a **Rewarded** ad unit for each app; note the **Ad Unit ID**.
   Optionally add a placement named `extra_moves` (capping/pacing can stay in
   the dashboard defaults — the client already caps at 2/attempt).
5. Turn on the **ironSource network** (bidding) for both apps. Skip other
   networks for launch; add more later for better fill.

### 2. Unity editor
1. `git checkout main && git pull`.
2. Window → Package Manager → Unity Registry → search **Ads Mediation**
   (`com.unity.services.levelplay`) → Install (9.5.x).
3. Paste the four dashboard values into the consts at the top of
   `Assets/_Scripts/Monetization/AdManager.cs`.
4. Project Settings → Player → **Android** tab → Scripting Define Symbols →
   add `LEVELPLAY_ENABLED`. Repeat on the **iOS** tab. (Do NOT add it for
   WebGL — WebGL ships ad-free this release by design.)
5. In the game scene: add a Button inside the game-over window
   (text: `Watch ad: +5 moves`), then on the `Match3UI` component assign it to
   **Watch Ad Extra Moves** and drag the Match3 board object into **Board**.
6. iOS only: LevelPlay's menu (Ads Mediation → Developer Settings) has the
   ATT/SKAdNetwork helpers; enable the ATT prompt.

### 3. Test before release
1. Dashboard → your app → **Test Devices**: register your phone's advertising ID.
2. Build to device, fail a level past level 3 → button appears → ad plays →
   +5 moves. Decline path: close the ad early → no moves granted.
3. `LevelPlay.ValidateIntegration()` can be called once from anywhere to dump
   an integration report to the device log if something misbehaves.

### 4. Store compliance (before submitting builds)
- Google Play: Data safety form — declare ads + device identifiers; set the
  "Contains ads" flag.
- App Store: App Privacy — declare Identifiers/Usage Data for advertising;
  ATT prompt must show before ads personalize.
- If the game targets kids/families, LevelPlay needs the COPPA flag set —
  tell me and I'll add the `SetMetaData` call.

## What stays OFF this release
- Banner ads: never (per the monetization plan).
- Interstitials: Phase 2 (code seam exists; needs a plan review first).
- WebGL ads: off by design; the browser build stays clean.
