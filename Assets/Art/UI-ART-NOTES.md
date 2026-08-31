# Xandria Gem Jam — UI art notes

Replacement art for the old `match3-game` repo assets, remade in one consistent style
for the current `Jeffrey3James/Match3` repo.

| File | Size | Purpose |
| --- | --- | --- |
| `ui_panel_bg.png` | 1024×1024 RGBA | 9-slice panel frame for popups, dialogs, HUD cards |
| `bg_main_menu_portrait.png` | 864×1821 | Main menu background, portrait / mobile |
| `bg_main_menu_landscape.png` | 1672×941 | Main menu background, landscape / desktop WebGL |

Previews are in `previews/`.

---

## `ui_panel_bg.png` — 9-slice panel

### Unity import settings

| Setting | Value |
| --- | --- |
| Texture Type | Sprite (2D and UI) |
| Sprite Mode | Single |
| **Mesh Type** | **Full Rect** |
| Alpha Is Transparency | ✅ on |
| Wrap Mode | Clamp |
| Filter Mode | Bilinear |
| Max Size | 1024 |
| Generate Physics Shape | off |

`Mesh Type` **must** be `Full Rect`. The default `Tight` silently breaks 9-slicing,
because Unity trims the transparent corners out of the generated mesh and the border
regions no longer line up.

### Sprite Editor border

Open the sprite in the Sprite Editor and set all four border values to **288**:

```
L 288    T 288    R 288    B 288
```

### Image component

| Setting | Value |
| --- | --- |
| Image Type | Sliced |
| Fill Center | ✅ on |
| Pixels Per Unit Multiplier | `1` for a chunky frame, `2` for a slimmer one |

### Sizing rules

- **Minimum size: 576 × 576 px** (2 × 288). Below that the fixed corners overlap and
  the frame visibly collapses. Raise `Pixels Per Unit Multiplier` for smaller panels
  rather than shrinking the RectTransform past this.
- Corner gem clusters occupy the outer ~208 px of each corner. Inset content by about
  **220 px** horizontally near the top and bottom edges so labels don't collide with
  the gems. The straight edges only need ~60 px of clearance.

### Why this file is safe to stretch

The old `ui_panel_bg.png` could not be 9-sliced: it had gems baked into the middle of
every edge, so stretching smeared the heart gem and distorted the gold filigree. This
version was rebuilt so the geometry is exact, not approximate:

- The four edge strips are **pixel-identical along their length** (verified variance `0`),
  so horizontal and vertical stretching is mathematically lossless.
- The centre is a **single flat colour** (`#EAD9FA`), so it stretches in both axes with
  no banding. A vertical gradient was deliberately removed — a gradient cannot survive
  the left/right strips, which must be uniform per row, and would have produced a
  visible seam down both sides.
- Corner ornaments are fully contained inside the 288 px corner squares and are blended
  into the edge profiles, so every corner→edge seam measures **0** difference.
- Real alpha outside the rounded silhouette (the generator painted a fake checkerboard;
  it was replaced with a flood-filled mask and the white fringe was removed).

Stretch-tested at 1600×700, 700×1500, 700×700, 600×600, 1800×620 and 1400×600 with a
maximum seam discontinuity of 1/255.

---

## Palette

All values sampled directly from `ui_panel_bg.png`, not estimated.

| Role | Hex |
| --- | --- |
| Panel interior | `#EAD9FA` |
| Outer rim lavender | `#B2A3E5` |
| Rim highlight | `#F0E8FE` |
| Gold — specular | `#FDFFEC` |
| Gold — mid | `#FFE14C` |
| Gold — shadow | `#A65400` |
| Gold — outline | `#522816` |
| Gem pink | `#EF2B7E` |
| Gem green | `#20BC39` |
| Gem blue | `#1191F9` |

### Text on the panel interior

Measured WCAG contrast against the `#EAD9FA` interior:

| Colour | Ratio | Use |
| --- | --- | --- |
| `#4A2A6B` | 8.6 : 1 | headings, high emphasis |
| `#5C3484` | 6.9 : 1 | default body text |
| `#6E4A96` | 5.1 : 1 | secondary text |
| `#8060A5` | 3.8 : 1 | large text only — fails AA at body size |

Avoid pure white text on the interior; it fails contrast badly.

---

## Backgrounds

Both are painted with a calm, open middle so the logo, level buttons and HUD read
clearly on top. Detail is pushed to the top, bottom and outer edges.

Import as Sprite (2D and UI), `Max Size 2048`, compressed. Because the two orientations
are separate paintings rather than one image, pick one at runtime from the aspect ratio
rather than scaling a single background — stretching one into the other orientation
distorts the gems.

Note the native sizes: portrait is 864×1821 and landscape is 1672×941, so on a
1080×2400-class phone Unity will upscale roughly 1.25×. That is fine for soft painted
sky and cloud art, but say the word if you want them regenerated larger for crisper
detail on high-DPI screens.

## Not remade

The gems, icons (coin, life, star filled/empty) and `ui_button_primary` from the old
repo were left alone. They already sit in the same violet/gold/faceted-jewel family, so
they read as one set with this panel. If any of them get remade later, match the palette
table above.

---

## Open style question

This panel was styled from the art in the **old archived** `match3-game` repo — soft
airbrushed shading, glossy 3D-rendered gold filigree, heavy speculars.

The art actually live in **this** repo (`Assets/_MyGems/`) is a different family: **flat
vector / low-poly faceted**, bold saturated colour, hard-edged facets, white star glints,
no soft gradients. The world theme is woodland — mushroom and vine obstacles, acorn
hammer, and `ForestEmblem` power-up crests built from a slim gold arch with green leaves
and twigs.

So this panel is production-ready and geometrically correct, but it is **not** yet in the
same visual family as the live gems. If it should match them, the panel needs a restyle:
flat faceted corner gems, hard-edged gold, leaf-and-twig accents echoing the
`ForestEmblem` frames. The 9-slice geometry (288 border, flat centre, uniform edges) can
be preserved exactly through a restyle — only the painted surface changes.

Note also that `Assets/_MyGems_Chibi/` holds a full 19-asset chibi replacement set that is
committed but **not adopted** — it has no `.meta` files and no ScriptableObject references
point at it. Deciding between the flat faceted set and the chibi set should come before
locking the panel style.
