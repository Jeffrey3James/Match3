# AuthPanel — reusable UI Toolkit auth component

A drop-in login / register / guest / signed-in panel that talks directly to
`JadedBellesApiClient` and hydrates `PlayerDataManager` after login.

## Files

- `Assets/Resources/UI/Auth/AuthPanel.uxml` — markup. Both the signed-out (login/register/guest) view and the signed-in view live in one file. The controller toggles between them via `.signed-out` / `.signed-in` classes on `#auth-root`.
- `Assets/Resources/UI/Auth/AuthPanel.uss` — styling. Loaded at runtime by the controller from Resources, so no per-scene wiring is needed.
- `AuthPanelController.cs` — component. Requires a `UIDocument` on the same GameObject.
- `MainMenuAuthGate.cs` — token-check + auto-mount on MainMenu.

## Runtime setup (what `MainMenuAuthGate` does)

`MainMenuAuthGate` attaches BOTH a `UIDocument` and an `AuthPanelController` to its **own** GameObject — one GameObject, one modal panel. No second "AuthPanel (runtime)" child. Flow:

1. `MainMenuUI.Start()` calls `MainMenuAuthGate.Ensure()`.
2. If `TokenStore.HasSession()` is true → pulls remote saves and no overlay appears.
3. Else if the player previously chose guest (`jb_played_as_guest` PlayerPrefs) → no overlay.
4. Otherwise → the gate adds `UIDocument` + `AuthPanelController` to itself and loads the UXML/USS from Resources. The panel stays up until the player either signs in / registers or taps "Play as guest".

## Editor setup (if you want to place the panel manually in a scene)

1. Create an empty GameObject.
2. Add **UI Document**: assign your Panel Settings asset and set Source Asset to `AuthPanel.uxml`.
3. Add **Auth Panel Controller** on the same GameObject.

That's it. The controller loads `AuthPanel.uss` from Resources itself.

## What it does

- **Log in** → `JadedBellesApiClient.Login(email, password, …)`. On success, tokens are already saved by the client via `TokenStore`; the controller then calls `GetCurrentUser` to populate the signed-in view and (optionally) `PlayerDataManager.PullRemotePlayerData()` to sync progress.
- **Register** → `JadedBellesApiClient.Register(email, password, displayName, …)`, then the same post-login flow.
- **Play as guest** → does not touch the API; fires `OnGuestChosen`. `PlayerDataManager` is already local-first, so the game works offline.
- **Log out** → `JadedBellesApiClient.Logout(…)`. Even if the server call errors, the local session is treated as gone (client clears tokens).

## Notes

- `JsonUtility`-only via the existing API client — no Newtonsoft, keeps WebGL builds small.
- No editor-only APIs are touched, so it compiles for WebGL / Android / iOS without `#if UNITY_EDITOR` guards.
- Namespaces: `JadedBelles.Networking` for `JadedBellesApiClient`, global namespace for `PlayerDataManager`.
