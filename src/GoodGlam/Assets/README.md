# GoodGlam brand assets

`Logo.png` is the **canonical source** for the plugin's logo. It is embedded in the plugin for the
in-game floating button and About tab, and its repository URL is used as the Dalamud installer icon.

## Requirements

- PNG with an RGBA color model.
- Exactly 512x512 pixels. Dalamud requires installer icons to be square and no larger than 512x512.
- Transparent around the artwork. The in-game notification glow is generated from the PNG's alpha
  channel, so an opaque background would produce a square halo.
- Artwork must retain its intended proportions; fit and center non-square source art on the transparent
  canvas rather than stretching or cropping it.

The high-resolution source stays crisp when ImGui downscales it for different UI/DPI scales.

## Updating

Export a prepared PNG from the external design source, replace `Logo.png`, and verify it still meets
the requirements above. There is no generated raster artifact or vector source to keep in sync in this
repository.
