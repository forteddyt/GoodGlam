# GoodGlam brand assets

`Logo.svg` is the **canonical source** for the plugin's logo (a minimal split-pyramid mark adapted from Eorzea Collection's logo, in EC coral `#fb4b4e`). Every other logo artifact is generated from it — do not hand-edit the generated files.

## Generated artifacts

| File       | Purpose                                                              |
| ---------- | ------------------------------------------------------------------- |
| `Logo.png` | 512×512 transparent raster embedded in the plugin (`LogoWindow`). High-res so it stays crisp when ImGui downscales it at any DPI. |

ImGui can only draw raster textures, so the SVG is rasterized to `Logo.png` at build-prep time rather than at runtime (no SVG decoder ships with Dalamud).

## Regenerating

Requires [ImageMagick](https://imagemagick.org/) (`magick`). From the repo root:

```bash
./src/GoodGlam/Assets/regenerate-logo.sh
```

After editing `Logo.svg`, re-run the script and commit both the SVG and the regenerated PNG.
