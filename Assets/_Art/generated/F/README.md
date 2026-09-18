# Agent F — Generated Art Pack

Baseline art pack for the Match3 commercial polish sprint. All PNGs are RGBA with transparent backgrounds. Style: chibi/toy, purple + gold + cyan palette, soft dark outline, matches `Assets/_Characters/XandriaAndArmadillo.png`.

Generated with `gpt_image_1_5` via `asi-generate-image`, then resized/cropped to target dimensions with PIL LANCZOS.

## Baseline assets

| File | Size | Purpose |
| --- | --- | --- |
| `coin_particle_128.png` | 128×128 | Coin-fly animation particle (agent C) |
| `star_particle_128.png` | 128×128 | Star-fly animation particle (agent E) |
| `sparkle_particle_64.png` | 64×64 | Match burst particles (agent B) |
| `map_node_locked_192.png` | 192×192 | World map — locked level node (agent C) |
| `map_node_current_192.png` | 192×192 | World map — current/next level node (agent C) |
| `map_node_complete_192.png` | 192×192 | World map — completed level node (agent C) |
| `map_connector_line_64.png` | 64×256 | World map — vertical connector segment, tileable (agent C) |
| `speech_bubble_left_512.png` | 512×256 | Xandria companion speech bubble (agent C) |
| `panel_decorate_slot_before_320.png` | 320×320 | Decorate MVP — broken pedestal "before" (agent E) |
| `panel_decorate_slot_after_320.png` | 320×320 | Decorate MVP — restored pedestal "after" (agent E) |
| `booster_hammer_icon_192.png` | 192×192 | In-run booster bar — Hammer (agent D) |
| `booster_rocket_icon_192.png` | 192×192 | Pre-level booster tray — Rocket (agent D) |
| `booster_tnt_icon_192.png` | 192×192 | Pre-level booster tray — TNT (agent D) |
| `booster_lightball_icon_192.png` | 192×192 | Pre-level booster tray — Light Ball (agent D) |
| `butlers_gift_ribbon_512.png` | 512×160 | Streak reward banner — "XANDRIAS GIFT" (agent E) |

## Notes

- The decorate "before" pedestal intentionally has 5 stone variants worth of visual room; agent E can duplicate/tint per slot or file art requests for slot 2–5 variants.
- `map_connector_line_64.png` was generated as a taller column to allow vertical tiling; the top and bottom dashes may need a 1–2 px overlap when tiled — trim in Unity's slice inspector as needed.
- The ribbon text renders "XANDRIAS GIFT" (apostrophe omitted by the image model). Agent C/E may overlay a proper "XANDRIA'S GIFT" TMP label if the missing apostrophe matters.

## Unity import settings (recommendation)

- Texture Type: Sprite (2D and UI)
- Alpha Is Transparency: on
- Compression: None (small icons) or Normal Quality
- Filter Mode: Bilinear
- For `map_connector_line_64.png`: Wrap Mode Y = Repeat
