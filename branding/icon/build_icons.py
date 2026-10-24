"""Builds every FluentFlow icon asset from the geometry below.

    python build_icons.py

Needs Python 3.10+, Pillow, and Microsoft Edge (used headless to rasterise the SVGs). Writes into ./final.

The mark: FluentFlow's mirrored visualizer (five bars whose outline is a lens) with one wave of negative space flowing
through it. Above the wave the bars are warm light, below it they deepen. On the player's dark surface for the app
icon; on its own for the tray and for places that bring their own background.

Sizes up to 32 px are drawn on the pixel grid by hand (pixel_tuned), because the wave cut can't survive plain scaling
there: it lands on half pixels and crumbles the short outer bars. Instead the two tones meet where the wave crosses each
bar, so the wave is still there as a change of colour. From 40 px up the master, cut and all, scales cleanly.
"""
import os
import pathlib
import struct
import subprocess
import tempfile

from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
OUT = HERE / "final"
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"

# Geometry, on a 256 grid.
BAR_X = [56, 92, 128, 164, 200]
BAR_H = [56, 104, 152, 104, 56]
BAR_W = 22
WAVE = ((30, 136), (62, 108), (94, 108), (128, 128), (162, 148), (194, 148), (226, 120))  # two cubic segments
WAVE_D = "M 30,136 C 62,108 94,108 128,128 C 162,148 194,148 226,120"
CUT = 10  # width of the negative-space wave

# Colour.
WARM = ("#FCD8C4", "#F0A88E")        # above the wave
DEEP = ("#DE8268", "#A9473A")        # below the wave
TILE = ("#2E2433", "#141117")        # the player's surface
LIGHT_TILE = ("#FFF8F4", "#F1E5DE")
LIGHT_WARM = ("#EE9A7E", "#D9785E")
LIGHT_DEEP = ("#B9533F", "#8C372C")


def wave_y(x):
    """y of the wave at x (the curve is a function of x, so solve x(t) by bisection on the right segment)."""
    segment = WAVE[0:4] if x <= 128 else WAVE[3:7]
    (x0, y0), (x1, y1), (x2, y2), (x3, y3) = segment

    def bez(t, a, b, c, d):
        u = 1 - t
        return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d

    lo, hi = 0.0, 1.0
    for _ in range(40):
        mid = (lo + hi) / 2
        if bez(mid, x0, x1, x2, x3) < x:
            lo = mid
        else:
            hi = mid
    return bez(lo, y0, y1, y2, y3)


def bars(width=BAR_W):
    return "\n".join(
        f'      <rect x="{x - width / 2:g}" y="{128 - h / 2:g}" width="{width:g}" height="{h:g}" rx="{width / 2:g}"/>'
        for x, h in zip(BAR_X, BAR_H))


def gradient(id_, top, bottom, y1, y2):
    return (f'    <linearGradient id="{id_}" gradientUnits="userSpaceOnUse" x1="0" y1="{y1}" x2="0" y2="{y2}">\n'
            f'      <stop offset="0" stop-color="{top}"/><stop offset="1" stop-color="{bottom}"/>\n'
            f'    </linearGradient>')


def mark_svg(tile=None, warm=WARM, deep=DEEP, mono=None, title="FluentFlow"):
    """The scalable mark. tile: (top, bottom) colours of the rounded-square plate, or None for the bare mark.
    mono: one colour for everything (the monochrome variant), which keeps the wave as a cut."""
    defs = []
    body = []
    if tile:
        defs.append(gradient("ff-tile", tile[0], tile[1], 20, 236))
        defs.append('    <linearGradient id="ff-edge" x1="0" y1="0" x2="0" y2="1">\n'
                    '      <stop offset="0" stop-color="#FFFFFF" stop-opacity="0.22"/>'
                    '<stop offset="0.5" stop-color="#FFFFFF" stop-opacity="0.03"/>\n    </linearGradient>')
        body.append('  <rect x="20" y="20" width="216" height="216" rx="56" fill="url(#ff-tile)"/>')
        if tile == TILE:
            body.append('  <rect x="21" y="21" width="214" height="214" rx="55" fill="none" stroke="url(#ff-edge)" stroke-width="2"/>')
        else:
            body.append('  <rect x="20.5" y="20.5" width="215" height="215" rx="55.5" fill="none" stroke="#000000" stroke-opacity="0.08"/>')
    defs.append(f'    <mask id="ff-cut">\n      <rect width="256" height="256" fill="#FFFFFF"/>\n'
                f'      <path d="{WAVE_D}" fill="none" stroke="#000000" stroke-width="{CUT}" stroke-linecap="round"/>\n    </mask>')
    if mono:
        body.append(f'  <g mask="url(#ff-cut)" fill="{mono}">\n{bars()}\n  </g>')
    else:
        defs.append(gradient("ff-warm", warm[0], warm[1], 52, 140))
        defs.append(gradient("ff-deep", deep[0], deep[1], 120, 204))
        defs.append(f'    <clipPath id="ff-above"><path d="{WAVE_D} L 226,0 L 30,0 Z"/></clipPath>')
        defs.append(f'    <clipPath id="ff-below"><path d="{WAVE_D} L 226,256 L 30,256 Z"/></clipPath>')
        body.append('  <g mask="url(#ff-cut)">\n'
                    f'    <g clip-path="url(#ff-above)" fill="url(#ff-warm)">\n{bars()}\n    </g>\n'
                    f'    <g clip-path="url(#ff-below)" fill="url(#ff-deep)">\n{bars()}\n    </g>\n  </g>')
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">\n'
            f'  <title>{title}</title>\n  <defs>\n' + "\n".join(defs) + '\n  </defs>\n' + "\n".join(body) + '\n</svg>\n')


# Pixel-tuned small sizes: (bar x positions, bar width, bar heights, tile radius), all in whole pixels.
PIXEL = {
    16: ([1, 4, 7, 10, 13], 2, [4, 8, 12, 8, 4], 4),
    20: ([1, 5, 9, 13, 17], 2, [6, 10, 14, 10, 6], 5),
    24: ([3, 7, 11, 15, 19], 2, [6, 12, 18, 12, 6], 6),
    32: ([2, 8, 14, 20, 26], 4, [8, 16, 24, 16, 8], 8),
}


def pixel_tuned(size, tile=TILE, warm=WARM, deep=DEEP, mono=None, plate=True):
    """A hand-placed version for one small size: whole pixels only, the wave reduced to where it crosses each bar."""
    xs, w, hs, radius = PIXEL[size]
    centre = size / 2
    parts = []
    if plate:
        parts.append(f'  <rect width="{size}" height="{size}" rx="{radius}" fill="url(#p-tile)"/>')
    rects = []
    for x, h in zip(xs, hs):
        top, bottom = centre - h / 2, centre + h / 2
        # Where the wave crosses this bar, on the pixel grid.
        split = round(wave_y((x + w / 2) / size * 256) / 256 * size)
        split = min(max(split, top + 1), bottom - 1)
        upper = (top, split)
        lower = (split, bottom)
        upper_fill, lower_fill = (mono, mono) if mono else ("url(#p-warm)", "url(#p-deep)")
        # Rounded only at the outer ends: each half is a capsule clipped flat where the wave cuts it.
        r = w / 2
        rects.append(f'    <path d="{capsule_half(x, upper[0], w, upper[1] - upper[0], r, round_top=True)}" fill="{upper_fill}"/>')
        rects.append(f'    <path d="{capsule_half(x, lower[0], w, lower[1] - lower[0], r, round_top=False)}" fill="{lower_fill}"/>')
    defs = []
    if plate:
        defs.append(gradient("p-tile", tile[0], tile[1], 0, size))
    if not mono:
        defs.append(gradient("p-warm", warm[0], warm[1], centre - max(hs) / 2, centre))
        defs.append(gradient("p-deep", deep[0], deep[1], centre, centre + max(hs) / 2))
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {size} {size}" shape-rendering="geometricPrecision">\n'
            f'  <title>FluentFlow {size} px</title>\n  <defs>\n' + "\n".join(defs) + '\n  </defs>\n'
            + "\n".join(parts) + '\n  <g>\n' + "\n".join(rects) + '\n  </g>\n</svg>\n')


def capsule_half(x, y, w, h, r, round_top):
    """A bar segment rounded at one end (top or bottom) and square at the other, where the wave cuts it."""
    if h <= 0:
        return ""
    r = min(r, h)
    if round_top:
        return (f"M{x},{y + r} A{r},{r} 0 0 1 {x + w},{y + r} V{y + h} H{x} Z")
    return (f"M{x},{y} H{x + w} V{y + h - r} A{r},{r} 0 0 1 {x},{y + h - r} Z")


def rasterise(svg_text, size, out_png):
    html = ("<!doctype html><html><head><meta charset='utf-8'><style>html,body{margin:0;padding:0;background:transparent;"
            f"overflow:hidden}}svg{{display:block;width:{size}px;height:{size}px}}</style></head><body>{svg_text}</body></html>")
    with tempfile.NamedTemporaryFile("w", suffix=".html", delete=False, encoding="utf-8") as page:
        page.write(html)
    try:
        subprocess.run([EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                        "--default-background-color=00000000", f"--window-size={size},{size}",
                        f"--screenshot={os.path.abspath(out_png)}", pathlib.Path(page.name).as_uri()],
                       check=True, capture_output=True, timeout=60)
    finally:
        os.unlink(page.name)


def write_ico(path, pngs):
    """A multi-size .ico the way Windows' own icons are built: 32-bit bitmaps (with alpha) below 256 px, which every
    icon loader reads, and a PNG for 256 px, which keeps the file small."""
    entries = []
    for png in sorted(pngs, key=lambda p: Image.open(p).width):
        image = Image.open(png).convert("RGBA")
        size = image.width
        data = pathlib.Path(png).read_bytes() if size >= 256 else dib(image)
        entries.append((size, data))
    header = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    directory = b""
    blobs = b""
    for size, data in entries:
        dim = 0 if size >= 256 else size
        directory += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        blobs += data
    pathlib.Path(path).write_bytes(header + directory + blobs)


def dib(image):
    """An icon bitmap: a BITMAPINFOHEADER (height doubled for the mask), BGRA rows bottom-up, then an all-clear AND
    mask (the alpha channel does the masking)."""
    size = image.width
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, size * size * 4, 0, 0, 0, 0)
    rows = []
    pixels = image.load()
    for y in range(size - 1, -1, -1):
        row = bytearray()
        for x in range(size):
            r, g, b, a = pixels[x, y]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    mask_stride = ((size + 31) // 32) * 4
    return header + b"".join(rows) + bytes(mask_stride * size)


def main():
    for folder in ("", "png", "small", "variants", "tray"):
        (OUT / folder).mkdir(parents=True, exist_ok=True)

    master = mark_svg(tile=TILE)
    (OUT / "FluentFlow.svg").write_text(master, encoding="utf-8", newline="\n")
    rasterise(master, 1024, OUT / "FluentFlow.png")

    # The app icon at every size: hand-tuned up to 32, the master above.
    app_pngs = []
    for size in (16, 20, 24, 32):
        svg = pixel_tuned(size)
        (OUT / "small" / f"FluentFlow-{size}.svg").write_text(svg, encoding="utf-8", newline="\n")
        rasterise(svg, size, OUT / "png" / f"FluentFlow-{size}.png")
    for size in (40, 48, 64, 96, 128, 256, 512):
        rasterise(master, size, OUT / "png" / f"FluentFlow-{size}.png")
    for size in (16, 20, 24, 32, 40, 48, 64, 96, 128, 256):
        app_pngs.append(OUT / "png" / f"FluentFlow-{size}.png")
    write_ico(OUT / "FluentFlow.ico", app_pngs)

    # Variants.
    variants = {
        "FluentFlow-dark": mark_svg(tile=TILE),
        "FluentFlow-light": mark_svg(tile=LIGHT_TILE, warm=LIGHT_WARM, deep=LIGHT_DEEP),
        "FluentFlow-transparent": mark_svg(),
        "FluentFlow-mono-black": mark_svg(mono="#000000"),
        "FluentFlow-mono-white": mark_svg(mono="#FFFFFF"),
    }
    for name, svg in variants.items():
        (OUT / "variants" / f"{name}.svg").write_text(svg, encoding="utf-8", newline="\n")
        rasterise(svg, 1024, OUT / "variants" / f"{name}.png")
    # The monochrome master for tools that recolour it (one shape, currentColor).
    (OUT / "variants" / "FluentFlow-mono.svg").write_text(mark_svg(mono="currentColor"), encoding="utf-8", newline="\n")

    # Tray: a flat glyph like the other notification-area icons; white for a dark taskbar, black for a light one.
    for theme, colour in (("dark-taskbar", "#FFFFFF"), ("light-taskbar", "#1B1B1B")):
        pngs = []
        for size in (16, 20, 24, 32):
            svg = pixel_tuned(size, mono=colour, plate=False)
            (OUT / "tray" / f"FluentFlow-tray-{theme}-{size}.svg").write_text(svg, encoding="utf-8", newline="\n")
            png = OUT / "tray" / f"FluentFlow-tray-{theme}-{size}.png"
            rasterise(svg, size, png)
            pngs.append(png)
        write_ico(OUT / "tray" / f"FluentFlow-tray-{theme}.ico", pngs)


if __name__ == "__main__":
    main()
