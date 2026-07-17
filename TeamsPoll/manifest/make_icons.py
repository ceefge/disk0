#!/usr/bin/env python3
"""Generate the two Teams app icons with the standard library only.

color.png   : 192x192, accent background with a white bar-chart glyph.
outline.png : 32x32, transparent background with a white bar-chart glyph.

Run:  python3 make_icons.py
"""
import struct
import zlib

ACCENT = (79, 107, 237, 255)      # #4F6BED
WHITE = (255, 255, 255, 255)
CLEAR = (0, 0, 0, 0)


def write_png(path, width, height, pixels):
    """pixels: list of rows, each a list of (r,g,b,a) tuples."""
    raw = bytearray()
    for row in pixels:
        raw.append(0)  # filter type 0 (None) per scanline
        for (r, g, b, a) in row:
            raw += bytes((r, g, b, a))

    def chunk(tag, data):
        out = struct.pack(">I", len(data)) + tag + data
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return out + struct.pack(">I", crc)

    sig = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)  # 8-bit RGBA
    idat = zlib.compress(bytes(raw), 9)
    with open(path, "wb") as f:
        f.write(sig)
        f.write(chunk(b"IHDR", ihdr))
        f.write(chunk(b"IDAT", idat))
        f.write(chunk(b"IEND", b""))


def bar_chart(width, height, bg, fg):
    """A simple three-bar chart glyph centred in the canvas."""
    px = [[bg for _ in range(width)] for _ in range(height)]

    margin = width * 0.22
    inner_w = width - 2 * margin
    inner_h = height - 2 * margin
    base_y = height - margin           # bottom baseline of the bars

    n = 3
    gap = inner_w * 0.12
    bar_w = (inner_w - (n - 1) * gap) / n
    heights = [0.5, 0.8, 1.0]          # relative bar heights

    for i in range(n):
        x0 = margin + i * (bar_w + gap)
        x1 = x0 + bar_w
        top = base_y - inner_h * heights[i]
        for y in range(height):
            for x in range(width):
                if x0 <= x < x1 and top <= y < base_y:
                    px[y][x] = fg
    return px


def main():
    write_png("color.png", 192, 192, bar_chart(192, 192, ACCENT, WHITE))
    write_png("outline.png", 32, 32, bar_chart(32, 32, CLEAR, WHITE))
    print("wrote color.png (192x192) and outline.png (32x32)")


if __name__ == "__main__":
    main()
