"""Draws the launcher icons: a house holding a shelf of books.

Same format the firmware's own app icons and Better Stats use: 106x128, 8-bit BMP with a
16-level grey palette. The focused variant is the glyph knocked out of a black disc.

    python make_icons.py      # writes homelib.bmp and homelib_f.bmp next to this file
"""

from pathlib import Path

from PIL import Image, ImageDraw

W, H = 106, 128
S = 8  # supersampling factor, for clean anti-aliased edges after downscaling
HERE = Path(__file__).parent


def glyph(draw: ImageDraw.ImageDraw, ink: int, paper: int) -> None:
    def p(x, y):
        return (x * S, y * S)

    stroke = 7 * S
    # Roof: a thick chevron with a flat-cut apex.
    draw.line([p(19, 62), p(53, 30), p(87, 62)], fill=ink, width=stroke, joint="curve")
    # Walls.
    draw.line([p(28, 56), p(28, 96), p(78, 96), p(78, 56)], fill=ink, width=6 * S, joint="curve")
    # Books on the floor of the house: three upright spines with label bands, so they
    # read as books rather than a bar chart, and one leaning on them.
    for x0, top in ((33, 66), (43, 60), (53, 69)):
        draw.rectangle([p(x0, top), p(x0 + 8, 93)], fill=ink)
        draw.rectangle([p(x0 + 2, top + 5), p(x0 + 6, top + 7)], fill=paper)
        draw.rectangle([p(x0 + 2, 86), p(x0 + 6, 88)], fill=paper)
    draw.polygon([p(63, 93), p(70, 93), p(74, 69), p(67, 68)], fill=ink)


def render(focused: bool) -> Image.Image:
    big = Image.new("L", (W * S, H * S), 255)
    draw = ImageDraw.Draw(big)
    if focused:
        r = 50
        draw.ellipse([((53 - r) * S, (64 - r) * S), ((53 + r) * S, (64 + r) * S)], fill=0)
        glyph(draw, 255, 0)
    else:
        glyph(draw, 0, 255)
    return big.resize((W, H), Image.LANCZOS)


def to_bmp(img: Image.Image, path: Path) -> None:
    """8 bits per pixel with a 16-entry grey palette, written by hand: PIL always emits a
    256-entry palette, and the firmware's own icons carry exactly 16."""
    import struct

    levels = 16
    greys = [255 - round(i * 255 / (levels - 1)) for i in range(levels)]
    index = [min(range(levels), key=lambda i: abs(greys[i] - v)) for v in range(256)]

    row_size = (W + 3) & ~3
    pixels = bytearray()
    for y in range(H - 1, -1, -1):  # BMP rows run bottom-up
        row = bytes(index[img.getpixel((x, y))] for x in range(W))
        pixels += row + b"\0" * (row_size - W)

    palette = b"".join(struct.pack("<BBBB", g, g, g, 0) for g in greys)
    offset = 14 + 40 + len(palette)
    header = struct.pack("<2sIHHI", b"BM", offset + len(pixels), 0, 0, offset)
    dib = struct.pack("<IiiHHIIiiII", 40, W, H, 1, 8, 0, len(pixels), 3780, 3780, levels, levels)
    path.write_bytes(header + dib + palette + pixels)


if __name__ == "__main__":
    to_bmp(render(False), HERE / "homelib.bmp")
    to_bmp(render(True), HERE / "homelib_f.bmp")
    print("wrote", HERE / "homelib.bmp", HERE / "homelib_f.bmp")
