#!/usr/bin/env python3
"""Draws the app icon: a cartoon electric guitar, glossy, on a dark stage gradient.

Everything is drawn with signed-distance shapes and anti-aliased by supersampling, so the
icon needs no binary source art and regenerates identically on any machine with Pillow.

    python3 tools/make_icon.py            # writes Assets/Art/app_icon.png
    python3 tools/make_icon.py --all      # also refreshes the Gradle mipmap copies

The launcher copies under Library/ are build output; --all exists for the hand-relinked
builds that never run the Editor's icon step.
"""

import argparse
import math
import os

from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024
SS = 4  # supersample factor; drawn at SIZE*SS then reduced

# Palette: warm lacquer body against a cool stage, so the guitar reads at 48 px.
BODY_LIGHT = (255, 138, 66)
BODY_DARK = (206, 40, 46)
NECK_WOOD = (128, 84, 54)
FRET_SILVER = (214, 222, 232)
PICKUP_DARK = (28, 26, 34)
GOLD = (255, 202, 88)
STAGE_TOP = (54, 34, 92)
STAGE_BOTTOM = (14, 12, 30)


def rounded_bar(draw, p0, p1, width, fill):
    """A thick line with round caps: the guitar's neck and body outlines."""
    draw.line([p0, p1], fill=fill, width=int(width))
    r = width / 2.0
    for p in (p0, p1):
        draw.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=fill)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


def draw_guitar(size):
    """The guitar, drawn upright and then tilted, so neck, body and headstock stay joined.

    Drawing in a tilted frame by hand is what left a gap between the neck and the headstock:
    each part was rotated about its own centre. Rotating one finished upright drawing keeps
    every joint intact.
    """
    pad = 1.9  # room for the rotation so nothing clips
    canvas = int(size * pad)
    img = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def P(x, y):
        return (x * canvas, y * canvas)

    # --- Neck ---
    neck_w = 0.085
    d.rounded_rectangle(
        [P(0.5 - neck_w / 2, 0.150)[0], P(0.5 - neck_w / 2, 0.150)[1],
         P(0.5 + neck_w / 2, 0.640)[0], P(0.5 + neck_w / 2, 0.640)[1]],
        radius=int(0.012 * canvas), fill=NECK_WOOD + (255,))

    # Fretboard face.
    fb_w = neck_w * 0.64
    d.rounded_rectangle(
        [P(0.5 - fb_w / 2, 0.160)[0], P(0.5 - fb_w / 2, 0.160)[1],
         P(0.5 + fb_w / 2, 0.632)[0], P(0.5 + fb_w / 2, 0.632)[1]],
        radius=int(0.008 * canvas), fill=(48, 29, 21, 255))

    # Frets, wider apart toward the body.
    frets = 13
    for i in range(frets):
        t = ((i + 0.6) / frets) ** 0.92
        y = 0.618 - (0.618 - 0.170) * t
        d.line([P(0.5 - fb_w / 2, y), P(0.5 + fb_w / 2, y)],
               fill=FRET_SILVER + (255,), width=max(2, int(0.0085 * canvas)))

    # --- Body silhouette, built as a mask so it can carry a gradient ---
    mask = Image.new("L", (canvas, canvas), 0)
    md = ImageDraw.Draw(mask)
    md.ellipse([P(0.235, 0.560)[0], P(0.235, 0.560)[1], P(0.765, 0.985)[0], P(0.765, 0.985)[1]], fill=255)
    md.ellipse([P(0.285, 0.395)[0], P(0.285, 0.395)[1], P(0.715, 0.780)[0], P(0.715, 0.780)[1]], fill=255)
    # Waist pinch.
    md.ellipse([P(0.130, 0.640)[0], P(0.130, 0.640)[1], P(0.400, 0.850)[0], P(0.400, 0.850)[1]], fill=0)
    md.ellipse([P(0.600, 0.640)[0], P(0.600, 0.640)[1], P(0.870, 0.850)[0], P(0.870, 0.850)[1]], fill=0)

    mask = mask.filter(ImageFilter.GaussianBlur(0.0035 * canvas))
    mask = mask.point(lambda v: 255 if v > 128 else 0)
    mask = mask.filter(ImageFilter.GaussianBlur(0.0025 * canvas))

    grad = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grad)
    for y in range(canvas):
        t = y / float(canvas - 1)
        gd.line([(0, y), (canvas, y)],
                fill=lerp(BODY_LIGHT, BODY_DARK, min(1.0, max(0.0, (t - 0.42) * 1.15))) + (255,))
    grad.putalpha(mask)

    # Dark rim under the lacquer gives the body an edge at 48 px.
    rim_mask = mask.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(0.004 * canvas))
    rim = Image.new("RGBA", (canvas, canvas), (46, 10, 22, 255))
    rim.putalpha(rim_mask)
    img.alpha_composite(rim)
    img.alpha_composite(grad)

    # --- Hardware ---
    for cy in (0.760, 0.845):
        d.rounded_rectangle(
            [P(0.5 - 0.105, cy - 0.026)[0], P(0.5 - 0.105, cy - 0.026)[1],
             P(0.5 + 0.105, cy + 0.026)[0], P(0.5 + 0.105, cy + 0.026)[1]],
            radius=int(0.010 * canvas), fill=PICKUP_DARK + (255,))

    d.rounded_rectangle(
        [P(0.5 - 0.095, 0.920)[0], P(0.5 - 0.095, 0.920)[1],
         P(0.5 + 0.095, 0.960)[0], P(0.5 + 0.095, 0.960)[1]],
        radius=int(0.008 * canvas), fill=(198, 206, 216, 255))

    for cx, cy, r in ((0.640, 0.930, 0.026), (0.690, 0.870, 0.022)):
        d.ellipse([P(cx - r, cy - r)[0], P(cx - r, cy - r)[1],
                   P(cx + r, cy + r)[0], P(cx + r, cy + r)[1]], fill=GOLD + (255,))

    # --- Headstock ---
    d.rounded_rectangle(
        [P(0.5 - 0.070, 0.028)[0], P(0.5 - 0.070, 0.028)[1],
         P(0.5 + 0.070, 0.165)[0], P(0.5 + 0.070, 0.165)[1]],
        radius=int(0.022 * canvas), fill=NECK_WOOD + (255,))

    # Tuning pegs, three a side.
    for i in range(3):
        cy = 0.055 + i * 0.038
        for side in (-1, 1):
            px = 0.5 + side * 0.098
            d.ellipse([P(px - 0.015, cy - 0.015)[0], P(px - 0.015, cy - 0.015)[1],
                       P(px + 0.015, cy + 0.015)[0], P(px + 0.015, cy + 0.015)[1]],
                      fill=FRET_SILVER + (255,))

    # --- Strings ---
    for i in range(6):
        t = (i + 0.5) / 6.0
        x = 0.5 + (t - 0.5) * (neck_w * 0.66)
        d.line([P(x, 0.955), P(x, 0.040)], fill=(238, 242, 250, 200),
               width=max(1, int(0.0062 * canvas)))

    img = img.rotate(-33, resample=Image.BICUBIC, expand=False)

    # Centre the tilted guitar in the frame it will be composited into.
    bbox = img.split()[3].getbbox()
    tilted = img.crop(bbox)
    scale = 0.80 * size / max(tilted.size)
    tilted = tilted.resize((max(1, int(tilted.width * scale)),
                            max(1, int(tilted.height * scale))), Image.LANCZOS)

    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.alpha_composite(tilted, ((size - tilted.width) // 2, (size - tilted.height) // 2))
    return out


def draw_icon(size=SIZE):
    """Full icon: stage background, glow, guitar, then a gloss sweep over everything."""
    big = size * SS
    canvas = Image.new("RGBA", (big, big), (0, 0, 0, 255))
    d = ImageDraw.Draw(canvas)

    # Stage gradient, slightly diagonal so it does not look like a flat fill.
    for y in range(big):
        t = y / float(big - 1)
        d.line([(0, y), (big, y)], fill=lerp(STAGE_TOP, STAGE_BOTTOM, t) + (255,))

    # Warm spotlight behind the guitar, which is what makes the icon glow.
    glow = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    cx, cy, r = 0.46 * big, 0.60 * big, 0.52 * big
    gd.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 146, 70, 175))
    glow = glow.filter(ImageFilter.GaussianBlur(0.12 * big))
    canvas.alpha_composite(glow)

    # Guitar, scaled to leave a margin, then dropped in with a soft shadow.
    guitar = draw_guitar(big)
    shadow = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    shadow.putalpha(guitar.split()[3])
    shadow = shadow.filter(ImageFilter.GaussianBlur(0.030 * big))
    shadow.paste((0, 0, 0, 150), (0, 0), shadow.split()[3])
    canvas.alpha_composite(shadow)
    canvas.alpha_composite(guitar)

    # Gloss: a wide diagonal highlight across the top-left, plus a soft sheen band.
    sheen = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sheen)
    sd.polygon(
        [(0, 0), (big * 0.78, 0), (0, big * 0.72)],
        fill=(255, 255, 255, 46))
    sd.polygon(
        [(0, big * 0.80), (big * 0.34, 0), (big * 0.46, 0), (0, big * 0.92)],
        fill=(255, 255, 255, 30))
    sheen = sheen.filter(ImageFilter.GaussianBlur(0.012 * big))
    canvas.alpha_composite(sheen)

    # Vignette, so the edges fall off and the guitar holds the centre.
    vig = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    vd = ImageDraw.Draw(vig)
    vd.ellipse([-big * 0.25, -big * 0.25, big * 1.25, big * 1.25], fill=(0, 0, 0, 0))
    vd.rectangle([0, 0, big, big], fill=(0, 0, 0, 70))
    mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(mask).ellipse(
        [-big * 0.22, -big * 0.22, big * 1.22, big * 1.22], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(0.10 * big))
    vig.putalpha(Image.composite(Image.new("L", (big, big), 70), Image.new("L", (big, big), 0), mask))
    canvas.alpha_composite(vig)

    return canvas.resize((size, size), Image.LANCZOS).convert("RGB")


DENSITIES = {
    "ldpi": 36,
    "mdpi": 48,
    "hdpi": 72,
    "xhdpi": 96,
    "xxhdpi": 144,
    "xxxhdpi": 192,
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--all", action="store_true",
                        help="also refresh the Gradle launcher mipmaps")
    parser.add_argument("--root", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    args = parser.parse_args()

    icon = draw_icon(SIZE)

    art = os.path.join(args.root, "Assets", "Art", "app_icon.png")
    os.makedirs(os.path.dirname(art), exist_ok=True)
    icon.save(art, "PNG", optimize=True)
    print("wrote", art, icon.size)

    if not args.all:
        return

    res = os.path.join(args.root, "Library", "Bee", "Android", "Prj", "IL2CPP",
                       "Gradle", "launcher", "src", "main", "res")
    for density, px in DENSITIES.items():
        folder = os.path.join(res, "mipmap-" + density)
        if not os.path.isdir(folder):
            continue
        out = os.path.join(folder, "app_icon.png")
        icon.resize((px, px), Image.LANCZOS).save(out, "PNG", optimize=True)
        print("wrote", out, px)


if __name__ == "__main__":
    main()
