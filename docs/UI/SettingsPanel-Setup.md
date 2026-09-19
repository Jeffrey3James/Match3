# Settings Panel — Inspector Setup Guide

Follow these steps once in the Unity Editor to build the Toon Blast-style
Settings modal that hosts the 5 crystal-gem toggles and the Save-Your-Progress
sign-in section.

Everything wires up through Inspector drag-and-drop; no scripting required
beyond the already-shipped `SettingsPanel.cs`.

---

## Step 1 — Add the panel to the scene

1. Open `Assets/_Scenes/MainMenu.unity`.
2. In the Hierarchy, find the existing empty **Settings Menu** GameObject
   (child of the Main Menu Canvas).
3. Right-click it → **UI → Panel**, name the new child **SettingsPanel**.
4. Set the `RectTransform` on **SettingsPanel** to your preferred modal size
   (e.g. width `900`, height `1400`, anchored center-center).
5. Add the component **Settings Panel** (`Assets/_Scripts/UI/SettingsPanel.cs`)
   to the **SettingsPanel** GameObject.

> Do the same for `Assets/_Scenes/GameScene.unity` if you want Settings
> reachable mid-game from the in-game HUD gear button.

---

## Step 2 — Build the 5 toggle rows

Add a `Vertical Layout Group` on **SettingsPanel** so rows stack cleanly.

For each of the five toggle rows below, create a child GameObject named
**Row_XXX** containing:

- **Icon** — `UI → Image`, set `Source Image` to the matching sprite in
  `Assets/Images/UI_Icons/` (see table).
- **Label** — `UI → Text - TextMeshPro`, e.g. `"Music"`.
- **Toggle** — `UI → Toggle`. Position it right-aligned in the row.

| Row              | Toggle field on SettingsPanel | Icon sprite (drag into `Icon` Image)         | Suggested label   |
|------------------|-------------------------------|-----------------------------------------------|-------------------|
| Row_Music        | `Music Toggle`                | `Assets/Images/UI_Icons/icon_music_on.png`         | `Music`           |
| Row_Haptic       | `Haptic Toggle`               | `Assets/Images/UI_Icons/icon_haptic_on.png`        | `Vibration`       |
| Row_Chat         | `Chat Toggle`                 | `Assets/Images/UI_Icons/icon_chat_on.png`          | `Chat`            |
| Row_LastSeen     | `Last Seen Toggle`            | `Assets/Images/UI_Icons/icon_lastseen_on.png`      | `Show Last Seen`  |
| Row_Notifications| `Notifications Toggle`        | `Assets/Images/UI_Icons/icon_notifications_on.png` | `Notifications`   |

Once each row exists:

1. Select **SettingsPanel** in the Hierarchy.
2. In the Inspector, drag each row's **Toggle** into the matching field under
   the `Toggles` header on the **Settings Panel** component.

> The panel initializes each toggle from `PlayerPrefs` in `Awake()` and writes
> back on change. Default for a fresh install is **ON** for every toggle.

---

## Step 3 — Save Your Progress section

Below the toggles, add a header label **"Save Your Progress"** and then two
sibling GameObjects the panel will show/hide based on session state:

### Signed Out Group

Create GameObject **SignedOutGroup** with:

- **Button** `Sign In`
- **Button** `Sign Up`

### Signed In Group

Create GameObject **SignedInGroup** with:

- **TMP Text** `SignedInLabel` (leave text empty; the panel writes
  `"Signed in as {email}"` at runtime)
- **Button** `Sign Out`
- (optional) **Button** `Delete Account`

### Wire them

On the **Settings Panel** component, under `Save Your Progress`:

| Field                | Drag in                              |
|----------------------|--------------------------------------|
| `Signed Out Group`   | the **SignedOutGroup** GameObject    |
| `Signed In Group`    | the **SignedInGroup** GameObject     |
| `Sign In Button`     | the **Sign In** Button               |
| `Sign Up Button`     | the **Sign Up** Button               |
| `Sign Out Button`    | the **Sign Out** Button              |
| `Signed In Label`    | the **SignedInLabel** TMP text       |

Then under `Panel References`:

| Field                     | Drag in                                                     |
|---------------------------|-------------------------------------------------------------|
| `Login Panel`             | the existing **LoginPanel** GameObject in the scene         |
| `Account Settings Panel`  | (optional) the existing **AccountSettingsPanel** GameObject |
| `Delete Account Button`   | (optional) the `Delete Account` Button                      |

> Do **not** wire the button `OnClick()` lists in the Inspector.
> `SettingsPanel.Awake()` adds all listeners itself; wiring them twice would
> fire each action twice — same footgun as `LoginPanel` (see its class doc).

---

## Step 4 — Close button (optional but recommended)

Add a small **✕ Close** Button to the top-right corner of **SettingsPanel**
and drag it into the `Close Button` slot. That's enough — the panel hides
itself on click.

---

## Step 5 — Trigger the panel

The gear icon on the HUD should call `SettingsPanel.SetActive(true)` when
tapped. You already have the gear button in **GameScene** and **MainMenu**;
wire its `OnClick()` to the **SettingsPanel** GameObject's
`GameObject.SetActive` with `true`.

---

## Reading toggle state from game code

Anywhere in game code, read the current setting with the static accessors:

```csharp
if (SettingsPanel.MusicEnabled)         musicSource.UnPause(); else musicSource.Pause();
if (SettingsPanel.HapticEnabled)        HapticFeedback.Fire();
if (SettingsPanel.ChatEnabled)          chatDock.SetActive(true);
if (SettingsPanel.LastSeenEnabled)      PresenceService.BroadcastLastSeen();
if (SettingsPanel.NotificationsEnabled) PushService.Register();
```

No reference to the panel is required — the accessors read `PlayerPrefs`
directly, so they work even when the modal is closed or not yet spawned.

---

## Troubleshooting

- **Toggles look off after tapping.** The row `Toggle` needs a `Background`
  and `Checkmark` graphic assigned like any Unity Toggle. This is separate
  from the crystal glyph, which sits in the row's `Icon` Image and is
  always visible.
- **Sign In does nothing.** `Login Panel` slot on Settings Panel is empty.
  Drag in the existing LoginPanel from the scene.
- **Signed-in label stays empty.** `SignedInLabel` slot is empty, or the
  user isn't actually signed in — check `SessionService.State` in the
  Console.
- **Panel doesn't appear.** Same class of bug as the guest LoginPanel one:
  ensure the parent Canvas has a non-zero `lossyScale`. PR #34 fixed both
  scenes but if you added Settings under a new Canvas, verify its scale is
  `(1, 1, 1)`.
