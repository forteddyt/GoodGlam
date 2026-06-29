#!/usr/bin/env bash
# Regenerates every raster logo artifact from the canonical Logo.svg.
# Requires ImageMagick (`magick`). Run from anywhere.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
src="$here/Logo.svg"

if ! command -v magick >/dev/null 2>&1; then
  echo "error: ImageMagick (magick) is required but not found on PATH." >&2
  exit 1
fi

# 512x512 transparent PNG embedded in the plugin and drawn (downscaled) by LogoWindow.
magick -background none "$src" -resize 512x512 -depth 8 "PNG32:$here/Logo.png"

echo "Regenerated: $here/Logo.png"
