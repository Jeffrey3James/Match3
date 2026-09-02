# Wiring Guide

Everything you need to drag in the Editor for the loading screen, auth, and end-of-level work. Two scenes to touch: **AuthSplashScreen** and **GameScene**.

Rule that applies everywhere: **never wire a Button's OnClick list in the Inspector.** Every script here adds its own listeners in `Awake`. Doing both fires each action twice.

---

## Scene 1 — AuthSplashScreen

### Hierarchy to build

```
Canvas  (Screen Space - Overlay)
├── LoadingScreen          <- LoadingScreen.cs        [full-screen panel]
│   ├── LogoImage          <- BreathingImage.cs       [Image, your art]
│   ├── BarFill            <- LoadingBar.cs           [Image, Type = Filled]
│   └── StatusText                                     [TextMeshProUGUI, optional]
└── LoginPanel             <- LoginPanel.cs           [DISABLED in scene]
    ├── UsernameField                                  [TMP_InputField]
    ├── PasswordField                                  [TMP_InputField]
    ├── LoginButton                                    [Button]
    ├── SignUpButton                                   [Button]
    ├── GuestButton                                    [Button, optional]
    └── StatusText                                     [TextMeshProUGUI, optional]

SessionBootstrap  <- already in the scene (renamed from AuthManager)
```

### 1. BreathingImage — on the logo

No drags needed. It finds the `Image` on its own GameObject.

| Field | Set to |
| --- | --- |
| Target Image | leave empty |
| Breath Duration | `4` |
| Inhale Ratio | `0.4` |
| Pulse Scale | on |
| Scale Amplitude | `0.05` |
| Pulse Alpha / Sway Rotation | off unless you want them |

Turn on Pulse Alpha *or* Sway Rotation, not both — together they read as jitter rather than breathing.

### 2. LoadingBar — on the fill image

**Set the Image component first:** Type = **Filled**. Fill Method and Origin get forced to Vertical/Bottom at runtime, so don't worry about those.

| Field | Set to |
| --- | --- |
| Fill Image | drag this same Image |
| Force Vertical Fill | on |
| Fill Speed | `0.35` (higher = faster crawl) |
| Apply Speed Cap To Final Fill | on |
| Percent Label | optional TMP text |

> If the bar doesn't move, the Image Type isn't Filled. That's the only way to get a zero-to-one fill amount.

### 3. LoadingScreen — on the panel root

| Field | Set to |
| --- | --- |
| Loading Bar | the LoadingBar |
| Breathing Image | the BreathingImage |
| Canvas Group | leave empty (auto-added) |
| Screen Root | leave empty (defaults to itself) |
| Status Label | optional TMP text |
| Minimum Display Seconds | `1.25` |
| Fade Out Seconds | `0.35` |
| Persist Across Scenes | **off** |

Leave Persist Across Scenes off here — the splash screen's job ends when MainMenu loads.

### 4. LoginPanel

| Field | Drag |
| --- | --- |
| Username Field | TMP_InputField (this is the **email**) |
| Password Field | TMP_InputField, Content Type = **Password** |
| Login Button | Button |
| Sign Up Button | Button |
| Guest Button | Button (optional) |
| Status Text | TextMeshProUGUI (optional) |
| Panel Root | leave empty |
| Resolve Session On Start | **off** |
| Pull Player Data On Login | on |
| Remember Guest Choice | on |

> **Disable the LoginPanel GameObject in the scene.** SessionBootstrap turns it on only when auth is actually needed. If you leave it enabled, returning players see it flash.

Leave Resolve Session On Start **off**. It's a fallback for scenes with no SessionBootstrap; turning it on here makes two things race to resolve the session.

### 5. SessionBootstrap

Already in the scene — it's the old AuthManager object with the script swapped.

| Field | Set to |
| --- | --- |
| Loading Screen | the LoadingScreen |
| Login Panel | the LoginPanel |
| Wait For Level Catalog | on |
| Wait For Player Data | on |
| Step Timeout Seconds | `15` |
| Next Scene | `MainMenu` |

### Check build settings

`AuthSplashScreen`, `MainMenu`, and `GameScene` all need to be in **File → Build Profiles → Scene List**, with the splash first. Next Scene is matched by name — a typo throws at runtime.

---

## Scene 2 — GameScene

### Hierarchy

```
Canvas
└── GameOverWindow         <- LevelResultPanel.cs
    ├── HeaderText                                     [TextMeshProUGUI]
    └── ButtonContainer                                [Horizontal or Vertical Layout Group]
        ├── NextLevelButton
        ├── RetryButton
        ├── MainMenuButton
        └── WatchAdButton
```

### 6. LevelResultPanel — on the game-over window root

| Field | Set to |
| --- | --- |
| Panel Root | leave empty |
| Button Container | the ButtonContainer RectTransform |
| Header Text | TextMeshProUGUI |
| Next Level Button | Button |
| Retry Button | Button |
| Main Menu Button | Button |
| Watch Ad Extra Moves Button | Button |
| Win Header | `LEVEL COMPLETE` |
| Loss Header | `OUT OF MOVES` |
| Final Level Header | `ALL LEVELS COMPLETE` |
| Retry Costs A Life | on (see below) |
| Next Level Costs A Life | on |
| Board | leave empty (auto-found) |

**The Layout Group is required, not optional.** Buttons are deactivated rather than greyed out, so without a layout group the win screen shows two buttons with a hole where Retry used to be.

Set the Layout Group's **Child Alignment** to Middle Center so 2- and 3-button states both stay centred.

### 7. Match3UI

One new slot. The old Game Over slots are gone.

| Field | Drag |
| --- | --- |
| Level Result Panel | the GameOverWindow |

If you forget, it self-finds one in the scene and logs an error if there isn't one — but drag it anyway, the search includes inactive objects and isn't free.

---

## Test it

| Test | How | Expect |
| --- | --- | --- |
| Fresh install | Clear PlayerPrefs, play from splash | Loading screen → login panel |
| Returning player | Sign in, stop, play again | Loading screen → **straight to MainMenu**, no login panel |
| Offline | Airplane mode, play again | Still signed in. Console: "Could not reach the API… keeping the saved login" |
| Guest | Tap Guest, stop, play again | Straight to MainMenu, no panel |
| Sign out | Logout, then replay | Login panel returns |
| Bar gating | Play on fast wifi | Bar still crawls, never snaps to full |
| Win | Beat a level | Next Level + Main Menu only. **No ad button** |
| Loss | Run out of moves | Retry + Main Menu + ad button (if eligible) |
| Final level | Win the last level in the catalog | Next Level hidden, header says ALL LEVELS COMPLETE |

Clear PlayerPrefs from **Edit → Clear All PlayerPrefs**, or delete keys `jb_access_token`, `jb_refresh_token`, `jb_played_as_guest`.

---

## If something's wrong

| Symptom | Cause |
| --- | --- |
| Bar never moves | Image Type isn't **Filled** |
| Bar fills instantly | Fill Speed too high, or you called `SetImmediate` |
| Stuck on loading screen forever | A step never completed. Console names it after 15s |
| Login panel flashes then hides | The GameObject was left **enabled** in the scene |
| Login panel never appears | Session resolved — that's correct. Sign out to see it |
| Buttons fire twice | OnClick wired in the Inspector *and* in code |
| Gap where a hidden button was | No Layout Group on Button Container |
| Never leaves splash | `Next Scene` typo, or MainMenu not in the Scene List |
| Logged out after playing offline | Report it — `SessionService` is supposed to prevent exactly this |

---

## Two decisions worth revisiting

**Retry now costs a life.** It used to reload the scene for free, which meant the life economy could be bypassed forever. If that was intentional, turn `Retry Costs A Life` off.

**`CountdownTimer` in `StroTheGoatUtils.cs` is dead code** — `AuthManager` was its only consumer. Harmless, but nothing calls it now.
