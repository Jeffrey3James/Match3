# Xandria Gem Adventure — UI Asset Kit

All PNGs, transparent backgrounds, ready to drop into `Assets/UI/` in Unity.
Import as Sprite (2D and UI), Alpha Is Transparency = on. For frames, cards, buttons, ribbons, nav bar, and HUD pill: set Mesh Type = Full Rect and configure 9-slice borders in the Sprite Editor.

## Folder Structure

### backgrounds/ — fullscreen scene backdrops (9:16 portrait)
- `bg-home-castle.png` — home screen: floating islands, crystal castle, gemstone path
- `bg-guild-stadium.png` — guild screen: stage platform, banners, confetti
- `bg-dream-sky.png` — collection / leaderboard / settings default: dreamy cloud sky

### buttons/
- `btn-green-primary.png` — wide green pill CTA (Play Now, price buttons)
- `btn-blue-secondary.png` — wide blue pill
- `btn-pink-accent.png` — wide pink pill (Sign Out)
- `btn-square-teal.png` — square teal icon button (settings grid, boosters)
- `btn-circle-teal.png` — circular teal icon button
- `btn-close-x.png` — red circular close button
- `btn-level-round-green.png` — big round Level button with gold outline + stars
- `tab-pill-states.png` — selected (pink) + unselected (lavender) tab pills side by side

### frames/
- `frame-modal-pink.png` — ornate pink+gold portrait modal (Settings)
- `frame-card-blue.png` — wide blue+gold card with corner gems
- `input-field.png` — gold outlined pill for text input
- `ribbon-pink.png` — pink ribbon banner with folded tails

### cards/
- `card-bundle-blue.png` — shop bundle row (blue outline, cream fill) — 9-slice
- `card-player-row.png` — leaderboard row (gold outline, cream fill) — 9-slice
- `card-fairy-frame.png` — collectible fairy card (gold ornate border, purple gem)

### nav/
- `nav-bar-bg.png` — bottom purple navigation bar background — 9-slice

### hud/
- `hud-bar-bg.png` — pink HUD pill for currency/lives — 9-slice
- `avatar-frame-gold.png` — round gold avatar frame ring (hollow center)

### icons/nav/ (bottom navigation)
- `icon-nav-home.png` (house)
- `icon-nav-shop.png` (bag+plus)
- `icon-nav-collection.png` (cards)
- `icon-nav-guild.png` (shield+swords)
- `icon-nav-leaderboard.png` (laurel+star)
- `icon-nav-settings.png` (gear)

### icons/settings/ (settings modal)
- `icon-set-sfx.png` (speaker)
- `icon-set-music.png` (note)
- `icon-set-haptics.png` (phone vibration)
- `icon-set-chat.png` (chat bubble)
- `icon-set-notifications.png` (bell)
- `icon-set-language.png` (globe)

### icons/powerups/ (match-3 boosters)
- `icon-powerup-bomb.png`
- `icon-powerup-rocket.png`
- `icon-powerup-hammer.png`
- `icon-powerup-rainbow.png` (rainbow gem)

### icons/ (currency + status)
- `icon-heart-life.png` — pink heart for lives
- `icon-gem-purple.png` — purple diamond gem currency
- `icon-coin-gold.png` — gold coin stack currency
- `icon-trophy-gold.png` — gold cup trophy

### characters/
- `xandria-portrait.png` — Xandria main portrait (canon: Black fairy, twin afro puffs, gold-rim glasses, jeweled crown, iridescent wings, gradient petal dress)
- `xandria/` — original 5 canon reference art files (idle, win, lose, hurt, cheer) for future generations
- `gem-golem.png` — pink crystal golem companion
- `owl-familiar.png` — white+purple starry owl familiar

### decor/
- `decor-crown-gold.png` — standalone gold crown with pink/purple gems
- `decor-treasure-chest.png` — open ornate chest with pink gem and light beams
- `decor-sparkle-small.png` — small four-point sparkle
- `decor-starburst-gold.png` — large gold multi-point burst with glow
- `decor-flourish-gold.png` — horizontal gold divider flourish
- `decor-badge-scallop.png` — scalloped starburst badge (pink+gold, empty center)
- `scrim-dark.png` — semi-transparent dark overlay (put behind modals)

## Scene Assembly Cheat Sheet

**Home:** `bg-home-castle` → `hud-bar-bg` + heart/gem/coin icons on top → 3 characters in center → `btn-level-round-green` with "Level N" TMP text → `nav-bar-bg` + 5 nav icons at bottom.

**Shop:** `bg-dream-sky` (tint pink) → `ribbon-pink` header with "Shop" TMP → 3× `tab-pill-states` for Bundles/Gems/Boosters → 3× `card-bundle-blue` rows with chest thumbnail + `btn-green-primary` price → 6× `btn-square-teal` with power-up icons → `nav-bar-bg`.

**Collection:** `bg-dream-sky` → `ribbon-pink` header → 5× `card-fairy-frame` with fairy portraits inside → `decor-treasure-chest` centered → `frames/ribbon-pink` bottom with unlock text → `btn-green-primary` "Play Now" → `nav-bar-bg`.

**Guild:** `bg-guild-stadium` → `ribbon-pink` header → `ribbon-pink` (purple tinted) with unlock text → `btn-green-primary` "Join a Guild" → 3 fairy characters on stage → `nav-bar-bg`.

**Leaderboard:** `bg-dream-sky` → `ribbon-pink` header → 3× `tab-pill-states` for Friends/Players/Guilds → 6× `card-player-row` with `avatar-frame-gold` on left + name TMP + score TMP → `btn-green-primary` "Connect Friends" → `nav-bar-bg`.

**Settings:** `scrim-dark` full-screen → `frame-modal-pink` centered → `btn-close-x` top-right of frame → 6× `btn-square-teal` with settings icons → 2× `input-field` for Help + Language rows → row with `avatar-frame-gold` + "Signed in as" TMP + `btn-pink-accent` "Sign Out" → red pill "Delete My Account".

## Character Canon (locked)

Xandria and every human/fairy in this game is a Black character with rich dark brown skin. Reference art lives in `characters/xandria/`. Never generate her (or other fairies) without passing those references.
