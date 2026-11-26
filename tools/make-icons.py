#!/usr/bin/env python3
"""Draws PlayRoom's home screen icons in code and writes them to src/PlayRoom.Server/wwwroot/icons/.

Usage: python3 tools/make-icons.py

The icon: a deep teal square with a rounded light game pad and, on it, a play triangle (left) and two buttons
(right). Each file is an opaque RGB PNG (colour type 2, no alpha), 4x4 supersampled. Standard library only.
"""
import os
import struct
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "src", "PlayRoom.Server", "wwwroot", "icons")
SIZES = {"apple-touch-icon.png": 180, "icon-192.png": 192, "icon-512.png": 512}
BACK = (16, 78, 84)
PAD = (236, 244, 240)
MARK = (232, 120, 64)
BUTTON = (16, 78, 84)
SAMPLES = 4


def in_round_rect(x, y, left, top, right, bottom, radius):
    """True when (x, y) lies in the rounded rectangle."""
    if x < left or x > right or y < top or y > bottom:
        return False
    cx = min(max(x, left + radius), right - radius)
    cy = min(max(y, top + radius), bottom - radius)
    return (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2


def in_triangle(x, y, a, b, c):
    """True when (x, y) lies in the triangle a, b, c."""
    def side(p, q):
        return (x - q[0]) * (p[1] - q[1]) - (p[0] - q[0]) * (y - q[1])
    d1, d2, d3 = side(a, b), side(b, c), side(c, a)
    has_neg = d1 < 0 or d2 < 0 or d3 < 0
    has_pos = d1 > 0 or d2 > 0 or d3 > 0
    return not (has_neg and has_pos)


def colour_at(u, v):
    """The colour at (u, v) in the unit square."""
    if in_triangle(u, v, (0.25, 0.37), (0.25, 0.63), (0.45, 0.50)):
        return MARK
    for bx, by in ((0.63, 0.44), (0.72, 0.56)):
        if (u - bx) ** 2 + (v - by) ** 2 <= 0.05 ** 2:
            return BUTTON
    if in_round_rect(u, v, 0.12, 0.26, 0.88, 0.74, 0.16):
        return PAD
    return BACK


def pixels(size):
    """Rows of RGB bytes for an icon of the size."""
    rows = []
    step = 1.0 / (size * SAMPLES)
    for y in range(size):
        row = bytearray([0])
        for x in range(size):
            r = g = b = 0
            for sy in range(SAMPLES):
                for sx in range(SAMPLES):
                    c = colour_at((x * SAMPLES + sx + 0.5) * step, (y * SAMPLES + sy + 0.5) * step)
                    r, g, b = r + c[0], g + c[1], b + c[2]
            n = SAMPLES * SAMPLES
            row += bytes((r // n, g // n, b // n))
        rows.append(bytes(row))
    return b"".join(rows)


def chunk(kind, data):
    """One PNG chunk."""
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def png(size):
    """The PNG bytes of an icon of the size."""
    header = struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(pixels(size), 9)) + chunk(b"IEND", b""))


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, size in SIZES.items():
        with open(os.path.join(OUT, name), "wb") as f:
            f.write(png(size))
        print("wrote", name, size)


if __name__ == "__main__":
    main()
