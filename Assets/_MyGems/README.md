# Xandria Gem Jam — Gem, Obstacle & Powerup Art

The PNGs in this folder are the **chibi replacement set**, adopted in place so
every ScriptableObject reference (all Gem, Obstacle and PowerUp SOs point at the
GUIDs in the `.meta` files here) continues to resolve without editing scenes.

## Style spec (for anything remade later)

Kawaii chibi jewel characters, all 1024x1024 RGBA on transparent background.

- Faceted crystal or organic bodies with hard triangular planes
- Two large glossy anime eyes with white catchlights; small friendly mouth
- Deep saturated color, dark navy/plum shadows in the lower half, bright rim highlights up top
- White star glints scattered across the body (4-point sparkles)
- No soft airbrushed shading; keep facet edges hard
- Woodland accents for powerups: leaves, twigs, weathered gold arch frame (see the `ForestEmblem` crests)

## Palette per character (sampled from the live art)

| Gem | Primary | Deep shadow | Highlight |
| --- | --- | --- | --- |
| Heart (yellow/red) | #F26A22 | #7A1A1A | #FFE87A |
| Circle (violet cabochon) | #A75BD7 | #40216A | #E8D3FF |
| Pink Triangle | #E93C9A | #6A1E52 | #FFC8E4 |
| Green Triangle | #34C244 | #0A5A1E | #B8F0A0 |
| Square (cyan) | #37B8F5 | #0B3A78 | #E4F6FF |
| Butterfly | #FFA733 / #35C2F0 / #9B3AC7 | #2D0E4A | #FFE4A8 |

## Adopting a change

Overwrite the PNG in place. Do not rename or move; `.meta` files must stay
paired with their PNG or Unity will regenerate a new GUID and every SO
reference will break.
