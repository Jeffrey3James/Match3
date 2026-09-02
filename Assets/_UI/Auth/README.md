# AuthPanel — reusable UI Toolkit auth component

A drop-in login / register / guest / signed-in panel that talks directly to
`JadedBellesApiClient` and hydrates `PlayerDataManager` after login.

## ⚠ Known issue (JadedBelles ecosystem, 2026-09-01)

Fully-runtime auth panel wiring (`Resources.Load` + `AddComponent<UIDocument>` +
`AddComponent<AuthPanelController>`) has been flaky across the JadedBelles game
ecosystem in this Unity 6 project: `UIDocument.rootVisualElement` does not
consistently contain the cloned visual tree by the time our controller binds,
even with a one-frame `yield return null` in `OnEnable`. Symptom is a repeated
`[AuthPanel] Could not find #auth-root` error and no panel showing.

**Workaround (do this in every scene that needs auth, starting with MainMenu):**

1. Add an empty GameObject named `MainMenuAuthGate`.
2. Add the `MainMenuAuthGate` component to it.
3. In the Inspector, drag:
   - `Assets/Resources/UI/Auth/AuthPanel.uxml` → **Uxml Asset**
   - `Assets/Resources/UI/Auth/AuthPanel.uss`  → **Uss Asset**
   - (Optional) a `PanelSettings` asset → **Panel Settings** — leave empty to have one built at runtime.
4. Save the scene.

The `MainMenuUI.Start()` hook still calls `MainMenuAuthGate.Ensure()`, but if a
scene-placed gate already exists that call becomes a no-op and the placed one
is used. Attach-at-runtime is still supported as a fallback but is not the
reliable path.

Track this as **JADED-UI-001** in whatever tracker you're using; revisit after
the next Unity 6 minor.

## Files

- `Assets/Resources/UI/Auth/AuthPanel.uxml` — markup. Signed-out (login/register/guest) and signed-in views in one file, toggled by `.signed-out` / `.signed-in` classes on `#auth-root`.
- `Assets/Resources/UI/Auth/AuthPanel.uss` — styling.
- `AuthPanelController.cs` — the panel component. Requires a `UIDocument` on the same GameObject. Has Inspector slots for UXML/USS as a first-choice source (Resources is a fallback).
- `MainMenuAuthGate.cs` — token check + panel mount. Has Inspector slots for UXML/USS/PanelSettings.

## Runtime flow

1. `MainMenuUI.Start()` calls `MainMenuAuthGate.Ensure()`.
2. If a scene-placed `MainMenuAuthGate` exists, it is reused. Otherwise a runtime GameObject is created.
3. If `TokenStore.HasSession()` → pull remote saves, no overlay.
4. Else if the player previously chose guest (`jb_played_as_guest` PlayerPrefs) → no overlay.
5. Otherwise → the gate attaches `UIDocument` + `AuthPanelController` to itself using the Inspector-dragged UXML/USS (or Resources fallback) and shows the panel until the player signs in / registers or taps "Play as guest".

## What it does

- **Log in** → `JadedBellesApiClient.Login(email, password, …)`. Tokens are saved by the client via `TokenStore`. Controller then calls `GetCurrentUser` for the signed-in view and (optionally) `PlayerDataManager.PullRemotePlayerData()`.
- **Register** → `JadedBellesApiClient.Register(email, password, displayName, …)`, then same post-login flow.
- **Play as guest** → does not touch the API; fires `OnGuestChosen`. `PlayerDataManager` is local-first, so the game works offline.
- **Log out** → `JadedBellesApiClient.Logout(…)`. Local session is cleared even if the server call errors.

## Notes

- `JsonUtility` only, via the existing API client — no Newtonsoft; WebGL friendly.
- No editor-only APIs, so it compiles for WebGL / Android / iOS without `#if UNITY_EDITOR` guards.
- Namespaces: `JadedBelles.Networking` for `JadedBellesApiClient`, global namespace for `PlayerDataManager`.
