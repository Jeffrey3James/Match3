# Chibi UI art — Xandria Gem Jam

Chibi kawaii style deliverables aligned to the live gem art in `Assets/_MyGems/`.

## Style

- Chibi cartoon kawaii mobile match-3
- Faceted low-poly gems with hard triangular planes
- Big anime eyes, small mouth, warm blush cheeks
- Deep saturated jewel color with dark plum / navy shadows in lower half
- Bright rim highlights along the top
- White 4-point star sparkles scattered on the body
- Woodland accents: brown vine + leaves + weathered gold rope frames
- No airbrush; hard edges only

## Files

### Panel — `UI/ui_panel_bg.png`
- 1024×1024 RGBA, 9-slice with `spriteBorder = (288, 288, 288, 288)`
- Twisted vine + gold rope edges, chibi gem token (pink heart, blue teardrop, green emerald, purple cabochon) in each corner, small leaves along the vines
- Flat lavender interior `#EAD9FA` matching the world palette
- Center and edge strips are stretch-safe: interior is a solid color, edge strips are constant along the stretch axis
- Alpha is transparent outside the frame silhouette so it can drop onto any background

**Usage**
- Assign as the `Source Image` of a UI Image, set `Image Type = Sliced`, `Pixels Per Unit Multiplier = 1`
- Anchor / size the RectTransform to your popup dimensions; anything ≥ 576×576 stays clean
- Panel already includes a subtle inner soft edge so text sits well without a second layer

### Main menu backgrounds — `Backgrounds/`
- `bg_main_menu_portrait.png` — 9:16 orientation, chibi enchanted forest at twilight, aurora sky, chibi gems on the ground in the foreground
- `bg_main_menu_landscape.png` — 16:9 orientation, chibi enchanted forest at twilight, chibi gems in the foreground corners
- Both keep a wide calm zone in the middle for the UI stack (title, buttons, HUD)
- Import as Sprite (Single), no 9-slice border

## Palette (sampled from live art)

| Role | Hex | Notes |
| --- | --- | --- |
| Interior fill | `#EAD9FA` | Flat lavender inside the panel |
| Deep body shadow | `#28184A` | Under chibi gems and edges |
| Vine brown | `#7A4A2B` | Rope / twig accent |
| Leaf green | `#3B8A34` | Chibi leaves around corners |
| Gold rail | `#D9B34A` | Weathered gold frame |
| Pink gem | `#E64F97` | HeartGem body |
| Blue gem | `#3EA9F5` | SquareGem body |
| Green gem | `#4CD24C` | TriangleGem body |
| Violet gem | `#8B5CF6` | CircleGem body |

## Regenerating

Panel builder: `/home/user/workspace/art_chibi_panel/build.py`

- Loads chibi-styled generated source `panel_b.png`
- Flood-fills the fake-checkerboard transparent background to real alpha
- Downsamples 1254×1254 → 1024×1024
- Samples 25-px-wide edge profiles from the middle of each edge, then extends them constantly across the strip
- Fades the inner 20 rows of each strip toward the interior fill so edge → center seams are pixel-clean
- Ramp-blends the outer 40 px of each corner block toward the adjacent strip so corner → strip seams are ≤ 3 8-bit units
- Snaps the entire center 9-slice tile to the interior fill so alpha stays 255 and stretch stays flat
