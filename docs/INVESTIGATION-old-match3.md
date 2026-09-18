# Investigation — `old-match3`

**Investigated:** 2026-08-22  
**Scope:** Unity advertising/mediation, rewarded and interstitial callbacks, analytics/optimization, A/B or remote configuration, extra moves/continue/life mechanics, Packages, and project documentation.

## Executive finding

`/home/user/workspace/old-match3` has **no reusable ad SDK, mediation SDK, analytics SDK, remote-config/A/B-test system, rewarded/interstitial helper, or life system**. It is an earlier Unity 2022.3 project with a simple match-3 level loop, a login/ownership API client, and a basic win/lose HUD.

The practical reusable idea is its event-driven terminal-state shape—not ad code: `GameManager` exposes level-win/loss and moves-changed events, while `GameplayHUD` consumes win/loss events and owns retry/navigation UI. Xandria Gem Adventure already has comparable events and a richer existing life/economy system, so porting code from this project is not recommended.

## What was checked

| Area | Result | Evidence |
|---|---|---|
| Unity Ads / LevelPlay / IronSource / AppLovin MAX / Google AdMob | None found. | Required ad-keyword search returned no `.cs` matches; `Packages/manifest.json` contains only UGUI, TextMeshPro, and Unity core modules. |
| Rewarded or interstitial helpers/callback flows | None found. | No `Rewarded`, `Interstitial`, `Advertisement`, or provider-specific classes/usages in `Assets/Scripts`. |
| Unity Analytics / GameAnalytics / Firebase / Facebook / optimization SDKs | None found. | Required analytics-keyword search returned no `.cs` matches; no related package entries were present. |
| Remote config / A/B testing / feature flags | None found. | No configuration, experiment, variant, or optimization scaffolding was found in source or settings. |
| Extra moves / continue-after-fail | Not implemented. | The loss HUD only exposes retry/home and reloads the level on retry: `Assets/Scripts/UI/GameplayHUD.cs:161-168, 202-214`. |
| Life / hearts system | Not implemented. | The README lists a life icon only as art; there is no gameplay/persistence code for lives. |
| Docs | Read. | `README.md` and `Tools/level_system_implementation_report.txt` describe the Unity 2022.3 handoff, 200 levels, login/ownership flow, and data validation; neither describes monetization or telemetry. |

## Potentially useful structural references (not copy-ready ad code)

1. **Level state and move counter:** `Assets/Scripts/Core/GameManager.cs:12-23` keeps `MovesRemaining` and emits `OnMovesChanged`, `OnLevelWon`, and `OnLevelLost`. It initializes and decrements moves at lines `46-74` and `85-93`, and marks a loss when moves/time are exhausted at lines `160-166`.
2. **Terminal UI entry points:** `Assets/Scripts/UI/GameplayHUD.cs:91-112` subscribes to those events; its loss handler shows a loss panel at lines `161-168`. That is conceptually where a Continue/Extra Moves prompt would be inserted if this old codebase were revived.
3. **Retry behavior:** `GameplayHUD.HandleRetryPressed` resets the same level and reloads `Gameplay` at `Assets/Scripts/UI/GameplayHUD.cs:202-209`. A rewarded continue would require new GameManager API to clear terminal state and grant moves before this reload route; it does not exist today.
4. **Persistence:** `Assets/Scripts/Levels/LevelDatabase.cs:42-89` stores unlocked level and best stars in `PlayerPrefs`, but has no coins/lives/ad entitlement persistence.

## Package and platform notes

- The old project is pinned to **Unity 2022.3.50f1** (`ProjectSettings/ProjectVersion.txt`).
- `Packages/manifest.json` has no Unity Services, mediation, analytics, Firebase, or third-party ad dependency.
- Its README confirms scene assets were not included in the handoff, which makes direct prefab/UI reuse impractical.

## Comparison with Xandria Gem Adventure (current repo)

The current Unity 6 project has no ad or analytics SDK integrated either, but it does have more relevant foundations:

- a centralized event hub with `onLevelCompleted` / `onLevelFailed` (`Assets/_Scripts/GameEvents.cs:53-81`);
- a loss UI and retry/menu buttons (`Assets/_Scripts/UI/Match3UI.cs:87-91, 144-157`);
- move exhaustion that triggers `LevelFailed` (`Assets/_Scripts/Match3.cs:762-789`);
- coins and persisted player data (`Assets/_Scripts/Managers/PlayerHandler.cs:104-130`; `Assets/_Scripts/Utils/PlayerDataManager.cs:119-169`);
- a five-life data model and a 20-minute configured timer (`Assets/_Scripts/Managers/PlayerHandler.cs:16-18, 132-170`; initial values are created at `Assets/_Scripts/Utils/PlayerDataManager.cs:56-60`).

**Important current-repo implementation note:** `PlayerHandler.AddALifeToPlayer()` currently increments only when lives are already at least the maximum (`Assets/_Scripts/Managers/PlayerHandler.cs:167-170`), and the checked source does not contain a scheduled regeneration path. Fix and test that system before offering a rewarded “+1 life” placement.

## Conclusion

Do not migrate ad code from `old-match3`: there is none. Build a clean, current Unity 6 integration in Xandria Gem Adventure around its existing `GameEvents`, `Match3`, `Match3UI`, `PlayerHandler`, and `PlayerDataManager` boundaries. The implementation specification is in `AD-MONETIZATION-PLAN.md`.
