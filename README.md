# Xandria Gem Adventure

A match-3 puzzle game built in Unity 6, shipping to **mobile (Android/iOS)** and **the browser (WebGL)**. It is part of the [JadedBelles](https://jadedbelles.com) platform: players can play as a guest with zero friction, or sign in with their JadedBelles account to sync progress across devices.

- **Unity version:** `6000.0.34f1` (Unity 6) — Universal Render Pipeline
- **Product name:** Xandria Gem Adventure · **Bundle ID:** `com.JadedBelles.XandriaGemAdventure` · **Company:** JadedBelles
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

> ⚠️ **`JadedBellesApiClient.GameProductSlug` is currently `"match3-quest"`** — the seeded platform product slug. Confirm this matches the real production product for Xandria Gem Adventure and change that one constant if not.

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
    Networking/       JadedBellesApiClient, ApiModels, TokenStore
    Utils/            AuthManager, PlayerDataManager, Timer, StroTheGoatUtils
    Gems/ GemTypes/   Gem behaviour, gem/obstacle/power-up ScriptableObject types
    GridSystem/       Grid math
    UI/               Menus and HUD
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

## Known TODO

- **Login/register UI does not exist yet.** The plumbing is done — `JadedBellesApiClient` has `Login`, `Register`, and `Logout` ready — but the canvas needs building in the Editor and wiring to those calls.
- WebGL and iOS build profiles need to be created.
- Confirm `GameProductSlug` against the production product catalog.
