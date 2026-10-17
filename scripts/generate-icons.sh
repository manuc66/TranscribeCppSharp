#!/bin/bash
# Regenerate every raster icon from the one SVG source of truth.
#
# The icon is not hand-drawn per surface: `docs/assets/images/icon.svg` is the
# only authored file, and the PNGs, the Windows .ico and the desktop app's asset
# are all derived from it here. Re-run this after editing the SVG so no committed
# bitmap drifts from the vector.
#
# Requires: rsvg-convert (librsvg2-bin), and either Python 3 + Pillow or
# ImageMagick 7 (`magick`) to build the .ico.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="${REPO_ROOT}/docs/assets/images/icon.svg"
IMG_DIR="${REPO_ROOT}/docs/assets/images"
UI_ASSETS="${REPO_ROOT}/src/TranscribeCppSharp.Ui/Assets"

if ! command -v rsvg-convert >/dev/null 2>&1; then
    echo "Error: rsvg-convert not found. Install librsvg2-bin, then re-run." >&2
    exit 1
fi

if [[ ! -f "${SRC}" ]]; then
    echo "Error: icon source not found at ${SRC}" >&2
    exit 1
fi

mkdir -p "${UI_ASSETS}"

# Web / README / NuGet package icon (NuGet asks for 128x128).
rsvg-convert -w 512 -h 512 "${SRC}" -o "${IMG_DIR}/icon-512.png"
rsvg-convert -w 128 -h 128 "${SRC}" -o "${IMG_DIR}/icon-128.png"

# Window icon for the desktop app. 256 keeps it crisp on HiDPI; it is embedded
# as an AvaloniaResource, not shipped as a loose file.
rsvg-convert -w 256 -h 256 "${SRC}" -o "${UI_ASSETS}/icon.png"

# Multi-resolution Windows icon for the .exe (CLI and desktop app). Windows picks
# the closest size, so the small frames matter as much as the large one.
#
# Pillow is preferred: it stores every frame PNG-compressed (~26 KB here).
# ImageMagick's ICO coder writes BMP frames and cannot PNG-compress them, so its
# output is a valid but much larger file (~370 KB); it is only the fallback.
if python3 -c "import PIL.Image" >/dev/null 2>&1; then
    python3 - "${IMG_DIR}/icon-512.png" "${IMG_DIR}/icon.ico" <<'PY'
import sys
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
Image.open(src).save(dst, format="ICO", sizes=sizes)
PY
    echo "  .ico built with Pillow (PNG frames)"
elif command -v magick >/dev/null 2>&1; then
    magick "${IMG_DIR}/icon-512.png" \
        -define icon:auto-resize=256,128,64,48,32,24,16 "${IMG_DIR}/icon.ico"
    echo "  .ico built with ImageMagick (BMP frames, larger file)"
else
    echo "Error: need Python 3 + Pillow or ImageMagick to build the .ico." >&2
    exit 1
fi

echo "Regenerated icons from ${SRC#$REPO_ROOT/}:"
echo "  docs/assets/images/icon-512.png"
echo "  docs/assets/images/icon-128.png"
echo "  docs/assets/images/icon.ico"
echo "  src/TranscribeCppSharp.Ui/Assets/icon.png"
