# Xandria Gem Jam

A match-3 puzzle game built in Unity 6, shipping to **mobile (Android/iOS)** and **the browser (WebGL)**. It is part of the [JadedBelles](https://jadedbelles.com) platform: players can play as a guest with zero friction, or sign in with their JadedBelles account to sync progress across devices.

- **Unity version:** `6000.0.34f1` (Unity 6) — Universal Render Pipeline
- **Product name:** GemJam · **Bundle ID:** `com.JadedBelles.GemJam` · **Company:** JadedBelles
- **Backend:** [`jadedbelles-api`](https://github.com/Jeffrey3James/jadedbelles-api) at `https://api.jadedbelles.com`

---

## Quick start

1. Install Unity `6000.0.34f1` via Unity Hub.
2. Clone and open this folder as a Unity project. First import takes a while (it regenerates `Library/` and `Packages/packages-lock.json`).
3. Open `Assets/_Scenes/AuthSplashScreen.unity` and press Play. That scene is the boot scene — starting anywhere else will skip singleton setup and levels will not load.

### Required packages

All resolved automatically from `Packages/manifest.json`. The notable ones: Universal RP `17.0.3`, Input System `1.11.2`, TextMesh Pro, Adaptive Performance, and **DOTween** (vendored at `Assets/Plugins/Demigiant`, with the `DOTWEEN` define set in Project Settings).

---

## Boot flow

Three scenes are in the build, in order:

| Scene | Role |
| --- | --- |
| `AuthSplashScreen` | Boot scene. Hosts every persistent singleton, shows the logo for ~3s, restores a saved session silently in the background, then loads `MainMenu`. |
| `MainMenu` | Level select, player stats (lives, coins), navigation. |
| `GameScene` | The actual match-3 board. |

Two extra scenes are **not** in the build and must not be deleted:

- `LevelConfigScene` — the canvas the level editor renders its preview into.
- `Match3SceneTemplate _DO NOT DELETE_` — template for new board scenes.

On the splash screen, `AuthManager` runs a 3-second countdown and a session check in parallel. If a refresh token exists in `TokenStore`, it silently refreshes; otherwise the player continues as a **guest**. The menu loads only when both finish, so a slow network never blocks past the logo. Guests are never gated.

The newer `SessionBootstrap` + `LoadingScreen` pair does the same job with a visible, speed-gated progress bar and owns the decision of whether the login panel ever appears — see [Loading screen and auth UI](#loading-screen-and-auth-ui). Use one or the other on a given scene, not both.

---

## Levels are data-driven

Levels used to be ScriptableObjects, which meant a new level required a code-side asset and a full client rebuild. Now **all levels live in one JSON file** and can be shipped without rebuilding the game.

### Where levels come from, in order

1. **Remote:** `GET https://api.jadedbelles.com/api/v1/match3/levels` (anonymous, no auth). This is the live catalog.
2. **Bundled fallback:** `Assets/Resources/Levels/levels.json`, loaded via `Resources.Load<TextAsset>` when the request fails or the player is offline.

`LevelHandler` owns this. It exposes `LevelsReady`, an `OnLevelsReady` event, and `GetAllLevels()`; anything that reads levels must wait on one of those rather than assuming the catalog is present at `Awake`.

### Schema

```json
{
  "version": 1,
  "levels": [
    {
      "id": 0,
      "name": "Level 0",
      "maxMoves": 15,
      "width": 9,
      "height": 13,
      "excludedCells": [{ "x": 0, "y": 0 }],
      "objectives": [{ "gemType": "CircleGem", "amount": 5 }],
      "obstacles": [
        { "type": "Ice", "health": 2, "cells": [{ "x": 3, "y": 4 }] }
      ]
    }
  ]
}
```

- `excludedCells` — holes in the grid; cells that are not part of the playable board.
- `objectives` — win conditions. `gemType` must match a gem name registered in the registry.
- `obstacles` — `type` must match a registered obstacle name; `health` is hits to clear.

Valid `gemType` values: `ButterflyGem`, `CircleGem`, `GreenTriangleGem`, `HeartGem`, `SquareGem`, `TriangleGem`.
Valid obstacle `type` values: `Bubble`, `Grass`, `Ice`, `Mushroom`, `WitheredVine`.

Those strings are resolved to real assets by **`Assets/Resources/GemTypeRegistry.asset`**, which maps each name to its ScriptableObject. **If you add a new gem or obstacle, you must add it to that registry**, or levels referencing it will fail to hydrate.

### Editing levels

Use the editor window (**`Assets/_Scripts/LevelDesignEditorWindow.cs`**, editor-only). It reads and writes `Assets/Resources/Levels/levels.json` directly: level dropdown, add/duplicate/delete, grid size, move limit, and foldouts for excluded cells, obstacles, and objectives with dropdowns fed from the registry. It can also preview a level into `LevelConfigScene`.

You can also just edit the JSON by hand. It is the source of truth.

### Publishing new levels

This trips people up, so it is worth being explicit — there are **two** copies of the file:

| File | Purpose |
| --- | --- |
| `Assets/Resources/Levels/levels.json` (this repo) | Offline fallback baked into the build |
| `src/JadedBelles.Api/App_Data/match3-levels.json` (API repo) | What the live catalog serves |

To ship levels **without a client build**: copy the edited JSON over the API's `App_Data/match3-levels.json` and deploy the API. To also update the offline fallback, commit it here and cut a new build. The editor window logs a reminder about this when you save.

---

## Accounts and saves

There is no Unity Authentication, no Unity Cloud Save, and no Google Play Games in this project — all three were removed in favour of the JadedBelles API.

- **`Networking/JadedBellesApiClient.cs`** — the HTTP layer. Self-bootstrapping singleton via `Instance` (creates its own hidden GameObject, so no scene wiring needed), `UnityWebRequest` coroutines, and automatic token-refresh-then-retry-once on a `401`. Uses `JsonUtility` only — **Newtonsoft is not installed, do not add a dependency on it** (it also keeps WebGL builds small).
- **`Networking/TokenStore.cs`** — access/refresh tokens in `PlayerPrefs`, plus `HasSession()`.
- **`Utils/PlayerDataManager.cs`** — player data (name, level, lives, coins, life countdown). **Local-first:** `PlayerPrefs` under key `jb_player_data` is always the authority, so the game is fully playable offline and as a guest. Remote sync is best-effort on top.

### Save API

Saves are **generic across the whole platform** — one shared table keyed to player + product, not a per-game table:

| Method | Endpoint | Notes |
| --- | --- | --- |
| `GET` | `/api/v1/games/{slug}/saves` | All of the caller's slots for that game |
| `GET` | `/api/v1/games/{slug}/saves/{slot}` | One slot |
| `PUT` | `/api/v1/games/{slug}/saves/{slot}` | Upsert. Send `baseRevision` for conflict detection |
| `DELETE` | `/api/v1/games/{slug}/saves/{slot}` | Delete a slot |

The save payload is an **opaque JSON document owned by the game** — the API never inspects it, so changing the save shape needs no migration and no backend deploy. Cap is 256 KB per slot. This game uses slot `0`.

**Conflict handling:** every write bumps a server-side `Revision`. The client sends the last revision it saw as `baseRevision`; if it no longer matches, the API returns `409 Conflict` with the server's current save, and the client keeps whichever side has the newer `updatedAt`.

---

## Loading screen and auth UI

All of this is **plain uGUI**. There is no UI Toolkit anywhere in the project — no UXML, no USS, no `UIDocument`, no `Resources.Load` for UI. Runtime-attached UI Toolkit never bound reliably here (see [Gotchas](#gotchas)), so every reference was removed.

Five components, all Inspector drag-and-drop:

| Script | Role |
| --- | --- |
| `UI/BreathingImage.cs` | Organic pulse for the loading art — scale, optional alpha and rotation sway. |
| `UI/LoadingBar.cs` | Filled Image that fills upward, speed-gated against real progress. |
| `UI/LoadingScreen.cs` | Step registry and dismissal gating. Singleton via `LoadingScreen.Instance`. |
| `UI/SessionBootstrap.cs` | Runs session restore as a load step, then shows the login panel only if needed. |
| `UI/LoginPanel.cs` | Two fields, two buttons. Purely reactive — it does not decide when to appear. |
| `Networking/SessionService.cs` | Single source of truth for auth state. Not a MonoBehaviour. |

### Scene setup

**1. Loading screen.** Canvas → full-screen panel, `LoadingScreen` on the panel root.

- Child Image with your logo/art → add `BreathingImage`. Nothing to assign; it auto-finds the `Image` on its own GameObject.
- Child Image for the bar fill → add `LoadingBar`, drag that Image into **Fill Image**. Set Image **Type = Filled**. Fill Method and Origin are forced to Vertical/Bottom in `Awake`, so the bar always moves up even if you forget.
- Optionally drag a `TextMeshProUGUI` into **Status Label** (on `LoadingScreen`) and/or **Percent Label** (on `LoadingBar`).
- A `CanvasGroup` is added automatically if absent; it drives the fade-out.

**2. Login panel.** Canvas → panel, `LoginPanel` on the root. Drag in:

| Slot | Type | Notes |
| --- | --- | --- |
| Username Field | `TMP_InputField` | This is the account **email**. |
| Password Field | `TMP_InputField` | Set Content Type = **Password**. |
| Login Button | `Button` | |
| Sign Up Button | `Button` | Display name is derived from the email local part. |
| Guest Button | `Button` | Optional. |
| Status Text | `TextMeshProUGUI` | Optional. Shows errors. |

> **Do not wire the Buttons' OnClick lists in the Inspector.** `LoginPanel.Awake` adds its own listeners. Wiring both fires every action twice.

**3. Boot object.** Empty GameObject with `SessionBootstrap`. Drag in the `LoadingScreen` and the `LoginPanel`.

> **Leave the LoginPanel GameObject disabled in the scene.** `SessionBootstrap` activates it only when auth is genuinely required.

### How progress is gated

`LoadingBar` shows **`min(constant-speed ramp, real reported progress)`**. Two independent limits, both enforced every frame:

1. **Constant speed** — the fill moves at a fixed units-per-second rate (`fillSpeed`, default `0.35`). However fast the data actually arrives, the bar never jumps. A 20 ms load and a 3 s load look identical to the player until the ceiling moves.
2. **Cannot outrun the data** — the displayed value is clamped to reported progress. If only 40% of the work is confirmed, the bar stops dead at 40% regardless of elapsed time.

Progress only ever moves forward; a slow step finishing after a fast one cannot yank the bar backwards.

The screen then needs **three** gates before it dismisses:

- every registered step is complete,
- the bar has visually reached 100% at its own pace,
- `minimumDisplaySeconds` has elapsed (default `1.25`, prevents a flash on instant loads).

### Boot sequence

`SessionBootstrap` registers three steps up front so the denominator stays stable, then resolves them in order:

```
session   -> SessionService.Restore()
levels    -> waits on LevelHandler.LevelsReady
playerData-> PlayerDataManager.PullRemotePlayerData()  (skipped for guests)
```

Each completion raises the ceiling by 1/3 and the bar crawls toward it. Once the screen dismisses, `SessionBootstrap` checks `SessionService.IsResolved`:

- **Resolved** (signed in, offline-signed-in, or guest) → the login panel is never activated.
- **Needs auth** → the login panel is shown.

A returning player with a valid — or merely expired-but-refreshable — token never sees the login panel. Nothing else in the UI has to branch on auth state.

Every step has a timeout (`stepTimeoutSeconds`, default 15 s) so a hung request can't strand the player on the loading screen.

### Session state

`SessionService` is the only thing that decides whether the player is signed in. `LoginPanel` routes login, sign-up, guest, and logout through it rather than touching `PlayerPrefs` directly, so the guest flag isn't tracked in two places.

| State | Meaning | Login panel? |
| --- | --- | --- |
| `SignedIn` | Verified account session. | Hidden |
| `OfflineSignedIn` | Tokens exist, server unreachable. | Hidden |
| `Guest` | Player chose guest. | Hidden |
| `NeedsAuth` | No usable session. | **Shown** |
| `Unknown` | Restore hasn't run yet. | — |

**Persistent login.** `TokenStore` keeps access and refresh tokens in `PlayerPrefs` across launches, and `JadedBellesApiClient` refreshes on a `401` and retries once. `Restore()` calls `GET /api/v1/auth/me` to verify rather than trusting `HasSession()`, which only proves the token strings exist.

**Offline does not log you out.** A transport failure (DNS, timeout, no connection) resolves to `OfflineSignedIn` and keeps the tokens. Only a genuine auth rejection clears them. A player on a plane stays signed in.

### Driving the loading screen from your own code

```csharp
LoadingScreen.Instance.Show("Connecting...");
LoadingScreen.Instance.RegisterSteps("levels", "saves");

// ... later, as each finishes ...
LoadingScreen.Instance.CompleteStep("levels", "Levels loaded.");
LoadingScreen.Instance.CompleteStep("saves", "Progress synced.");
// screen dismisses itself once the bar catches up
```

For a long single step, `ReportPartialProgress(0..1)` fills within that step's slice. `ReportProgressDirect(0..1)` bypasses step tracking entirely. `ForceComplete()` finishes regardless of outstanding steps.

---

## Project layout

```
Assets/
  _Scenes/            AuthSplashScreen, MainMenu, GameScene, LevelConfigScene, template
  _Scripts/
    Match3.cs         Core board logic — matching, cascades, scoring (the big one)
    GameEvents.cs     Central event hub, consumed via GameEventsManager
    Level/            Level.cs — a board instance, hydrated at runtime from LevelData
    Levels/           LevelDataModels.cs (JSON DTOs), GemTypeRegistry.cs (name -> asset)
    Managers/         LevelHandler (level catalog), PlayerHandler, AudioManager
    Networking/       JadedBellesApiClient, ApiModels, TokenStore, SessionService
    Utils/            AuthManager, PlayerDataManager, Timer, StroTheGoatUtils
    Gems/ GemTypes/   Gem behaviour, gem/obstacle/power-up ScriptableObject types
    GridSystem/       Grid math
    UI/               Menus, HUD, LoginPanel, LoadingScreen/LoadingBar/BreathingImage,
                      SessionBootstrap.  All plain uGUI — no UI Toolkit.
    LevelDesignEditorWindow.cs, LevelEditorRuntime.cs
  Resources/          GemTypeRegistry.asset, Levels/levels.json  (runtime-loaded)
  _Prefabs/ _MyGems/ Images/ WebAssets/ Travis Game Assets/
  Plugins/Demigiant/  DOTween
Packages/             manifest.json (packages-lock.json is generated)
ProjectSettings/
```

`Assets/Resources/` is special: everything in it ships in every build and is loadable by name at runtime. The level JSON and gem registry live there deliberately.

---

## Building

### WebGL (browser)

No WebGL build profile exists in the repo yet — create one in **Build Profiles**. The code is already WebGL-safe: `UnityWebRequest` throughout, no `System.Net.Http`, no threads, `JsonUtility` instead of reflection-heavy JSON, and all editor-only code is behind `#if UNITY_EDITOR`. The API sends permissive CORS headers, so browser builds can call it from any origin.

Recommended: compression enabled, and be aware `PlayerPrefs` maps to browser IndexedDB — clearing site data wipes a guest's progress. That is the main argument for prompting guests to sign in.

### Android

A build profile exists at `Assets/Settings/Build Profiles/New Android™ Profile.asset`. Google Play Games sign-in was removed, so there is no Play Games dependency, no `androidlib`, and no External Dependency Manager to resolve.

### iOS

No profile yet — create one. Nothing in the codebase is iOS-hostile.

---

## Gotchas

- **Always launch from `AuthSplashScreen`.** Singletons and the level catalog are created there.
- **Editor code must be guarded.** Anything touching `UnityEditor` needs `#if UNITY_EDITOR` or device and WebGL builds will fail to compile. This has bitten this project before.
- **New gems/obstacles must be added to `Resources/GemTypeRegistry.asset`**, otherwise level JSON referencing them silently fails to hydrate.
- **`Assets.zip` (88 MB) is committed at the repo root.** It bloats every clone and is almost certainly a leftover backup. Consider deleting it and adding `*.zip` to `.gitignore`.
- **Don't assume levels are loaded.** Subscribe to `LevelHandler.OnLevelsReady` or check `LevelsReady`.
- **Don't reintroduce UI Toolkit.** Runtime-attached `UIDocument` + `VisualTreeAsset` repeatedly failed to bind its root here (tracked as JADED-UI-001) across several attempted fixes. Every UXML/USS asset and `UnityEngine.UIElements` reference was removed. Build UI with uGUI prefabs and Inspector drag-slots.
- **Don't wire button OnClick in the Inspector for `LoginPanel`.** It adds its own listeners in `Awake`; doing both fires each action twice.

## Known TODO

- **Loading screen and login panel canvases still need building in the Editor.** All the code exists and is Inspector-driven — see [Loading screen and auth UI](#loading-screen-and-auth-ui) for exactly which slots to fill.
- Decide whether `AuthManager`'s splash-screen session check is retired in favour of `SessionBootstrap`; right now both paths exist.
- WebGL and iOS build profiles need to be created.
- Google Play Console registration deadline is **Sept 30, 2026**. Package name `com.JadedBelles.GemJam`.
