# AuthPanel — reusable UI Toolkit auth component

A drop-in login / register / guest / signed-in panel that talks directly to
`JadedBellesApiClient` and hydrates `PlayerDataManager` after login.

## Files

- `AuthPanel.uxml` — markup. Contains both the signed-out (login/register/guest) view and the signed-in view; the controller toggles between them via `.signed-out` / `.signed-in` classes on `#auth-root`.
- `AuthPanel.uss` — styling. Referenced from the UXML with `<Style src="AuthPanel.uss" />`, so if you assign the UXML to a `UIDocument` the styles come along automatically.
- `AuthPanelController.cs` — behaviour. Requires a `UIDocument` on the same GameObject.

Place all three files together anywhere under `Assets/` (they reference each other by relative path).

## Setup per scene

1. In the scene hierarchy, create an empty GameObject (or reuse an existing canvas root) named `AuthPanel`.
2. Add component **UI Document**.
   - **Panel Settings:** any Panel Settings asset (create one via `Assets > Create > UI Toolkit > Panel Settings Asset` if the project doesn't already have one).
   - **Source Asset:** `AuthPanel.uxml`.
3. Add component **Auth Panel Controller**.
4. (Optional) In the Inspector:
   - `Hide When Signed In` — flip on for a boot-scene login gate; leave off for a persistent account widget in the main menu.
   - `Pull Player Data On Login` — leave on so lives / coins / progress refresh from the server right after sign-in.
   - `On Logged In` / `On Logged Out` / `On Guest Chosen` — wire scene transitions here if you want (e.g. `SceneManager.LoadScene("MainMenu")` on `OnLoggedIn` when used as a boot gate).

That's it. No prefab reconfiguration, no per-scene button wiring, no scene-file edits.

## What it does

- **Log in** → `JadedBellesApiClient.Login(email, password, …)`. On success, tokens are already saved by the client via `TokenStore`; the controller then calls `GetCurrentUser` to populate the signed-in view and (optionally) `PlayerDataManager.PullRemotePlayerData()` to sync progress.
- **Register** → `JadedBellesApiClient.Register(email, password, displayName, …)`, then the same post-login flow as above.
- **Play as guest** → does not touch the API; fires `OnGuestChosen`. `PlayerDataManager` is already local-first, so the game works fine offline.
- **Log out** → `JadedBellesApiClient.Logout(…)`. Even if the server call errors, the local session is treated as gone (the client clears tokens).

## Notes

- Uses only `JsonUtility`-friendly types via the existing API client — no Newtonsoft dependency (keeps WebGL builds small, consistent with the project's architecture rule).
- No editor-only APIs are touched, so it compiles for WebGL / Android / iOS without `#if UNITY_EDITOR` guards.
- Namespaces assumed: `JadedBelles.Networking` (for `JadedBellesApiClient`) and the global `PlayerDataManager`. If either lives elsewhere in your project, adjust the `using` line at the top of `AuthPanelController.cs`.
