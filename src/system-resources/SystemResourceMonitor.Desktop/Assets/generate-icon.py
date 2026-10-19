"""Regenerate app.ico: python generate-icon.py (needs Pillow).

The icon is drawn on a 16-unit grid and snapped to whole pixels for every size, so the
small sizes Windows uses for the tray and the taskbar stay sharp instead of being downscaled.
"""

from pathlib import Path

from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SUPERSAMPLE = 8

BACKGROUND_TOP = (42, 49, 66)
BACKGROUND_BOTTOM = (22, 27, 38)
TRACK = (255, 255, 255, 28)
# CPU, RAM, and limits, left to right: (x0, x1, top) on the 16-unit grid; every bar ends at y = 13.
BARS = [
    (3, 5, 8, (45, 212, 191)),
    (7, 9, 4, (96, 165, 250)),
    (11, 13, 6, (251, 191, 36)),
]
BAR_TOP_GRID = 3
BAR_BOTTOM_GRID = 13


def snap(unit: float, size: int) -> int:
    """Grid unit to a whole pixel of the target size, rounding half up."""
    return int(unit * size / 16 + 0.5)


def render(size: int) -> Image.Image:
    scale = SUPERSAMPLE
    canvas = size * scale
    image = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))

    # Vertical gradient clipped to a rounded square.
    gradient = Image.new("RGBA", (canvas, canvas))
    pixels = gradient.load()
    for y in range(canvas):
        t = y / (canvas - 1)
        row = tuple(round(a + (b - a) * t) for a, b in zip(BACKGROUND_TOP, BACKGROUND_BOTTOM)) + (255,)
        for x in range(canvas):
            pixels[x, y] = row
    mask = Image.new("L", (canvas, canvas), 0)
    radius = max(2, round(size * 0.1875)) * scale
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, canvas - 1, canvas - 1), radius=radius, fill=255)
    image.paste(gradient, (0, 0), mask)

    # Draw on a transparent layer and composite it, because drawing straight onto RGBA replaces alpha.
    layer = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    bottom = snap(BAR_BOTTOM_GRID, size) * scale - 1
    track_top = snap(BAR_TOP_GRID, size) * scale
    # Rounded bar ends only once a bar is wide enough for the curve to read.
    bar_radius = (snap(2, size) * scale) // 2 if size >= 32 else 0
    for x0, x1, top, colour in BARS:
        left = snap(x0, size) * scale
        right = snap(x1, size) * scale - 1
        if size >= 32:
            track = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
            ImageDraw.Draw(track).rounded_rectangle((left, track_top, right, bottom), radius=bar_radius, fill=TRACK)
            image.alpha_composite(track)
        draw.rounded_rectangle(
            (left, snap(top, size) * scale, right, bottom),
            radius=bar_radius,
            fill=colour + (255,),
        )

    image.alpha_composite(layer)
    return image.reduce(scale)


def main() -> None:
    here = Path(__file__).resolve().parent
    images = [render(size) for size in SIZES]
    largest = images[-1]
    largest.save(here / "app.ico", format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[:-1])


if __name__ == "__main__":
    main()
