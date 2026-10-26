# Prompt for ChatGPT image generation

Attach `style-reference.png` (a vanilla Elden Ring map icon) and, optionally, `current-icons.png`,
then paste the prompt below. Ask for one icon per message if it merges them into one image.

---

I'm making map icons for an Elden Ring mod. Match the style of the attached reference icon, which is
an icon from Elden Ring's world map: a hand-inked square frame with rough, slightly uneven edges, a
muted tan/parchment fill (around #B8925A), thick dark-brown ink outlines (around #2A1E12), flat
colors with no gradients, shading or text, and a faint soft light glow just outside the frame.

Make 4 separate icons, each a square 512x512 PNG with a **fully transparent background**. Each one is
the same framed square with a single bold, simple silhouette centered inside:

1. **weapon.png**: a straight sword pointing diagonally up and to the right.
2. **spell.png**: an open spellbook or a glowing glintstone with a few sparkle lines (it stands for
   both sorceries and incantations).
3. **armor.png**: a knight's great helm, facing forward.
4. **talisman.png**: a round talisman or medallion pendant hanging from a short chain.

Requirements:
- It must read clearly when shrunk to 32x32 pixels: thick shapes, few details, high contrast
  between the tan fill and dark outline.
- All four icons share an identical frame (same size, thickness and position) so they look like a set.
- The frame fills about 85% of the canvas, centered, with transparent padding for the glow.
- No text, no letters, no drop shadow, no background color or scenery.

---

---

## Seeker's Compass item icon (optional)

By default the compass uses the world map's compass rose. For an inventory-style picture instead,
attach a screenshot of an Elden Ring inventory item icon (e.g. the Telescope or Stonesword Key) and paste:

> Make an inventory item icon in the style of Elden Ring's item icons (attached example): a single
> painted object, realistic and slightly dark fantasy, lit from the upper left, centered, filling
> about 80% of the frame, on a **fully transparent background**, 512x512 PNG, no text, no border,
> no background scenery. The object is the "Seeker's Compass": an old hand-held brass compass,
> slightly tarnished, with a faint golden glow from the needle and a few engraved runes around
> the rim. It should read clearly when shrunk to 64x64 pixels.

Save it as `compass.png` here and rebuild.

---

Save the map-mark results here as `weapon.png`, `spell.png`, `armor.png` and `talisman.png`, then rebuild:

```
dotnet src/BuildCompass.Gen/bin/Release/net9.0/BuildCompass.Gen.dll build
```
