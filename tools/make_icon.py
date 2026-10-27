"""Generate the PromptPolisher application icon.

Renders a 1024px master PNG (rounded-square indigo gradient + two white
"polish" sparkles) and downsamples it into a multi-resolution .ico.

Usage:  python tools/make_icon.py
Output: assets/icon-1024.png
        src/PromptPolisher.App/Assets/app.ico
"""
from __future__ import annotations

import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024
SS = 4  # supersampling factor for the shape mask
ROOT = Path(__file__).resolve().parents[1]

TOP_LEFT = (99, 102, 241)    # indigo-500
BOTTOM_RIGHT = (67, 56, 202) # indigo-700
HIGHLIGHT = (165, 180, 252)  # indigo-300

ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]


def rounded_square_mask(size: int, radius_ratio: float = 0.225) -> Image.Image:
    big = size * SS
    mask = Image.new("L", (big, big), 0)
    draw = ImageDraw.Draw(mask)
    draw.rounded_rectangle((0, 0, big - 1, big - 1), radius=int(big * radius_ratio), fill=255)
    return mask.resize((size, size), Image.LANCZOS)


def diagonal_gradient(size: int, c0, c1) -> Image.Image:
    y, x = np.mgrid[0:size, 0:size]
    t = ((x + y) / (2.0 * (size - 1))).astype(np.float32)
    # ease-in so the darker corner stays dominant behind the white sparkle
    t = t ** 0.85
    arr = np.zeros((size, size, 3), dtype=np.uint8)
    for i in range(3):
        arr[..., i] = (c0[i] + (c1[i] - c0[i]) * t).astype(np.uint8)
    return Image.fromarray(arr, "RGB").convert("RGBA")


def sparkle_polygon(cx: float, cy: float, r: float, steps: int = 720) -> list[tuple[float, float]]:
    """Four-pointed star: r(θ) = R * (min + (1-min) * |cos 2θ|^p)."""
    pts = []
    for i in range(steps):
        theta = 2.0 * math.pi * i / steps
        radius = r * (0.09 + 0.91 * abs(math.cos(2.0 * theta)) ** 0.55)
        pts.append((cx + radius * math.cos(theta), cy + radius * math.sin(theta)))
    return pts


def main() -> None:
    mask = rounded_square_mask(SIZE)
    canvas = diagonal_gradient(SIZE, TOP_LEFT, BOTTOM_RIGHT)

    # soft top-left sheen for depth (invisible at 16px, nice at 256px)
    sheen = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(sheen).ellipse(
        (-int(SIZE * 0.45), -int(SIZE * 0.55), int(SIZE * 0.85), int(SIZE * 0.55)),
        fill=HIGHLIGHT + (46,),
    )
    sheen = sheen.filter(ImageFilter.GaussianBlur(SIZE * 0.06))
    canvas.alpha_composite(sheen)

    # white sparkles
    spark = Image.new("RGBA", (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
    sd = ImageDraw.Draw(spark)
    sd.polygon(sparkle_polygon(SIZE * SS * 0.455, SIZE * SS * 0.545, SIZE * SS * 0.315), fill=(255, 255, 255, 255))
    sd.polygon(sparkle_polygon(SIZE * SS * 0.735, SIZE * SS * 0.275, SIZE * SS * 0.145), fill=(255, 255, 255, 235))
    spark = spark.resize((SIZE, SIZE), Image.LANCZOS)
    canvas.alpha_composite(spark)

    canvas.putalpha(mask)

    png_path = ROOT / "assets" / "icon-1024.png"
    png_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(png_path)

    ico_path = ROOT / "src" / "PromptPolisher.App" / "Assets" / "app.ico"
    ico_path.parent.mkdir(parents=True, exist_ok=True)
    frames = [canvas.resize((s, s), Image.LANCZOS) for s in ICO_SIZES]
    frames[-1].save(ico_path, format="ICO", sizes=[(s, s) for s in ICO_SIZES])
    print(f"wrote {png_path}")
    print(f"wrote {ico_path} ({ico_path.stat().st_size / 1024:.1f} KB)")


if __name__ == "__main__":
    main()
