#!/usr/bin/env python3
"""Draws ShareHider's icons with the standard library only (no Pillow).

Writes src/ShareHider/Assets/ShareHider.ico (app + tray, idle) and
ShareHiderActive.ico (tray, while hiding). Each .ico holds 16, 20, 24, 32, 40,
48 and 64 px frames as uncompressed 32-bit DIBs, which every Windows API and
System.Drawing.Icon reads, plus a 256 px PNG frame for Explorer.

The design is a rounded tile holding a taskbar strip whose middle button is
missing: a taskbar with a gap where a hidden app would be.

    python3 scripts/make_icons.py
"""

import pathlib
import struct
import zlib

SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SUPERSAMPLE = 4
OUT = pathlib.Path(__file__).resolve().parent.parent / "src" / "ShareHider" / "Assets"

IDLE = ((0x00, 0x5F, 0xB8), (0x2B, 0x8C, 0xE0))    # Windows blue, top -> bottom
ACTIVE = ((0xC2, 0x4E, 0x00), (0xF2, 0x8C, 0x1A))  # amber: apps are being hidden
WHITE = (0xFF, 0xFF, 0xFF)


def rounded_rect(x, y, left, top, right, bottom, radius):
    """True when (x, y) lies inside the rounded rectangle."""
    if not (left <= x <= right and top <= y <= bottom):
        return False
    cx = min(max(x, left + radius), right - radius)
    cy = min(max(y, top + radius), bottom - radius)
    return (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2


def shade(u, v, gradient):
    """RGBA for a point in unit coordinates (0..1), or None when transparent."""
    if not rounded_rect(u, v, 0.03, 0.03, 0.97, 0.97, 0.22):
        return None
    top, bottom = gradient
    color = tuple(round(a + (b - a) * v) for a, b in zip(top, bottom))
    # The taskbar strip.
    if rounded_rect(u, v, 0.14, 0.56, 0.86, 0.82, 0.08):
        # Buttons are cut out of the strip; the middle slot is left solid: that app is hidden.
        in_left = rounded_rect(u, v, 0.21, 0.62, 0.37, 0.76, 0.03)
        in_right = rounded_rect(u, v, 0.63, 0.62, 0.79, 0.76, 0.03)
        if in_left or in_right:
            return color + (255,)
        return WHITE + (255,)
    # A window above the taskbar, drawn as an outline.
    if rounded_rect(u, v, 0.22, 0.16, 0.78, 0.48, 0.06) and not rounded_rect(u, v, 0.28, 0.24, 0.72, 0.42, 0.02):
        return WHITE + (255,)
    return color + (255,)


def render(size, gradient):
    """Anti-aliased RGBA rows, top row first."""
    rows = []
    n = SUPERSAMPLE
    for py in range(size):
        row = []
        for px in range(size):
            acc = [0, 0, 0, 0]
            for sy in range(n):
                for sx in range(n):
                    rgba = shade((px + (sx + 0.5) / n) / size, (py + (sy + 0.5) / n) / size, gradient)
                    if rgba:
                        a = rgba[3]
                        acc[0] += rgba[0] * a
                        acc[1] += rgba[1] * a
                        acc[2] += rgba[2] * a
                        acc[3] += a
            alpha = acc[3] // (n * n)
            if acc[3]:
                row.append((acc[0] // acc[3], acc[1] // acc[3], acc[2] // acc[3], alpha))
            else:
                row.append((0, 0, 0, 0))
        rows.append(row)
    return rows


def png(rows):
    size = len(rows)
    raw = b"".join(b"\x00" + bytes(c for px in row for c in px) for row in rows)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def dib(rows):
    """BITMAPINFOHEADER + bottom-up BGRA pixels + an all-zero AND mask (alpha does the masking)."""
    size = len(rows)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = b"".join(bytes((b, g, r, a)) for row in reversed(rows) for (r, g, b, a) in row)
    mask_row = ((size + 31) // 32) * 4
    return header + pixels + b"\x00" * (mask_row * size)


def ico(gradient):
    images = []
    for size in SIZES:
        rows = render(size, gradient)
        images.append((size, png(rows) if size >= 256 else dib(rows)))
    offset = 6 + 16 * len(images)
    directory, data = b"", b""
    for size, blob in images:
        dim = 0 if size >= 256 else size
        directory += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset + len(data))
        data += blob
    return struct.pack("<HHH", 0, 1, len(images)) + directory + data


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "ShareHider.ico").write_bytes(ico(IDLE))
    (OUT / "ShareHiderActive.ico").write_bytes(ico(ACTIVE))
    print(f"wrote {OUT / 'ShareHider.ico'} and {OUT / 'ShareHiderActive.ico'}")
