#!/usr/bin/env python3
"""Generate original pixel art PNGs for JAMM ModKit example packages (stdlib only)."""

import math
import os
import struct
import zlib

ROOT = os.path.dirname(os.path.abspath(__file__))


def write_png(path: str, width: int, height: int, pixels: list[tuple[int, int, int, int]]) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # filter type 0
        row_start = y * width
        for x in range(width):
            r, g, b, a = pixels[row_start + x]
            raw.extend((r, g, b, a))

    def chunk(tag: bytes, data: bytes) -> bytes:
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    idat = zlib.compress(bytes(raw), level=9)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", idat) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def blank(width: int, height: int) -> list[tuple[int, int, int, int]]:
    return [(0, 0, 0, 0)] * (width * height)


def set_px(buf: list[tuple[int, int, int, int]], w: int, h: int, x: int, y: int, c: tuple[int, int, int, int]) -> None:
    if 0 <= x < w and 0 <= y < h:
        buf[y * w + x] = c


def add_outline(buf: list[tuple[int, int, int, int]], w: int, h: int, outline: tuple[int, int, int, int]) -> None:
    src = list(buf)
    for y in range(h):
        for x in range(w):
            if src[y * w + x][3] != 0:
                continue
            for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                if 0 <= nx < w and 0 <= ny < h and src[ny * w + nx][3] > 0:
                    buf[y * w + x] = outline
                    break


def make_goldie() -> None:
    w, h = 64, 48
    px = blank(w, h)
    cx, cy = 26.0, 24.0
    for y in range(h):
        for x in range(w):
            dx = (x - cx) / 18.0
            dy = (y - cy) / 12.0
            in_body = dx * dx + dy * dy <= 1.0
            in_tail = 41 <= x <= 57 and abs(y - cy) <= (x - 39) * 0.72
            in_dorsal = 22 <= x <= 35 and 6 <= y <= 14 and (y - 6) >= (x - 28) * 0.35
            in_ventral = 24 <= x <= 33 and 34 <= y <= 40 and (40 - y) >= abs(x - 28) * 0.6
            if in_body or in_tail or in_dorsal or in_ventral:
                if in_tail or in_dorsal or in_ventral:
                    col = (255, 165, 40, 255)
                elif y < cy - 4:
                    col = (255, 225, 85, 255)
                elif y > cy + 4:
                    col = (235, 155, 35, 255)
                else:
                    col = (255, 198, 55, 255)
                set_px(px, w, h, x, y, col)
    # Scale highlights
    for sx, sy in ((23, 21), (28, 24), (33, 21), (31, 28)):
        set_px(px, w, h, sx, sy, (255, 248, 180, 255))
        set_px(px, w, h, sx + 1, sy, (255, 248, 180, 255))
    # Cute eye (left-facing)
    for y in range(h):
        for x in range(w):
            d = math.hypot(x - 15.5, y - 21.0)
            if d <= 3.2:
                set_px(px, w, h, x, y, (255, 255, 255, 255))
            if d <= 1.7:
                set_px(px, w, h, x, y, (25, 25, 35, 255))
    set_px(px, w, h, 14, 20, (255, 255, 255, 255))
    add_outline(px, w, h, (85, 48, 12, 255))
    write_png(os.path.join(ROOT, "example-fish", "art", "goldie.png"), w, h, px)


def make_glowfin() -> None:
    w, h = 76, 54
    px = blank(w, h)
    cx, cy = 32.0, 27.0
    for y in range(h):
        for x in range(w):
            dx = (x - cx) / 22.0
            dy = (y - cy) / 13.0
            in_body = dx * dx + dy * dy <= 1.0
            in_tail = 50 <= x <= 70 and abs(y - cy) <= (x - 48) * 0.78
            in_top_fin = 26 <= x <= 46 and 6 <= y <= 16 and (y - 6) >= abs(x - 36) * 0.4
            in_bot_fin = 28 <= x <= 42 and 38 <= y <= 46 and (46 - y) >= abs(x - 35) * 0.55
            if in_body or in_tail or in_top_fin or in_bot_fin:
                if in_tail or in_top_fin or in_bot_fin:
                    edge = (x > 63 or y < 10 or y > 42)
                    col = (130, 255, 245, 255) if edge else (40, 175, 205, 255)
                elif y < cy - 4:
                    col = (35, 115, 170, 255)
                else:
                    col = (60, 195, 215, 255)
                set_px(px, w, h, x, y, col)
    # Bioluminescent lateral spots
    for sx in (24, 31, 38, 45):
        for ox in range(2):
            for oy in range(2):
                set_px(px, w, h, sx + ox, 26 + oy, (170, 255, 240, 255))
    # Glowing eye (left-facing)
    for y in range(h):
        for x in range(w):
            d = math.hypot(x - 18.0, y - 24.0)
            if d <= 3.6:
                set_px(px, w, h, x, y, (200, 255, 255, 255))
            if d <= 1.8:
                set_px(px, w, h, x, y, (15, 35, 65, 255))
    add_outline(px, w, h, (15, 40, 75, 255))
    write_png(os.path.join(ROOT, "example-fish", "art", "glowfin.png"), w, h, px)


def make_upgrade_icon(filename: str, drawer) -> None:
    w, h = 50, 50
    px = blank(w, h)
    # Soft rounded badge interior
    for y in range(6, 44):
        for x in range(6, 44):
            if math.hypot(x - 24.5, y - 24.5) <= 21.0:
                set_px(px, w, h, x, y, (34, 44, 62, 230))
    drawer(px, w, h)
    add_outline(px, w, h, (18, 22, 32, 255))
    write_png(os.path.join(ROOT, "showcase-upgrades", "art", filename), w, h, px)


def draw_turbo(px, w, h):
    cx, cy = 24.5, 24.5
    for y in range(h):
        for x in range(w):
            d = math.hypot(x - cx, y - cy)
            ang = math.atan2(y - cy, x - cx)
            teeth = 15.5 + (2.5 if math.cos(ang * 6) > 0.35 else 0.0)
            if 6.0 <= d <= teeth:
                set_px(px, w, h, x, y, (250, 200, 55, 255))
            elif d < 6.0:
                set_px(px, w, h, x, y, (255, 245, 190, 255))


def draw_diver(px, w, h):
    for y in range(11, 39):
        for x in range(14, 36):
            dx = (x - 24.5) / 8.5
            dy = (y - 25.0) / 11.5
            if dx * dx + dy * dy <= 1.0:
                set_px(px, w, h, x, y, (70, 165, 250, 255))
    # Downward arrow highlight
    for y in range(16, 33):
        set_px(px, w, h, 24, y, (235, 250, 255, 255))
        set_px(px, w, h, 25, y, (235, 250, 255, 255))
    for k in range(5):
        set_px(px, w, h, 24 - k, 32 - k, (235, 250, 255, 255))
        set_px(px, w, h, 25 + k, 32 - k, (235, 250, 255, 255))


def draw_iron(px, w, h):
    for y in range(14, 37):
        for x in range(13, 37):
            half_w = 11.0 if y < 22 else max(4.0, 11.0 - (y - 22) * 0.5)
            if abs(x - 24.5) <= half_w:
                col = (195, 210, 228, 255) if y < 24 else (140, 155, 175, 255)
                set_px(px, w, h, x, y, col)
    for y in range(17, 25):
        set_px(px, w, h, 24, y, (255, 255, 255, 255))


def draw_magnet(px, w, h):
    cx, cy = 24.5, 23.0
    for y in range(12, 39):
        for x in range(11, 39):
            d = math.hypot(x - cx, y - cy)
            if y <= cy and 7.0 <= d <= 13.0:
                set_px(px, w, h, x, y, (230, 65, 65, 255))
            elif y > cy and (11 <= x <= 17 or 32 <= x <= 38) and y <= 36:
                col = (225, 235, 245, 255) if y >= 30 else (230, 65, 65, 255)
                set_px(px, w, h, x, y, col)


def draw_golden(px, w, h):
    cx, cy = 24.5, 24.5
    for y in range(h):
        for x in range(w):
            d = math.hypot(x - cx, y - cy)
            if d <= 13.5:
                col = (255, 225, 70, 255) if d <= 10.5 else (230, 170, 30, 255)
                set_px(px, w, h, x, y, col)
    # Star/sparkle center
    for k in range(-5, 6):
        set_px(px, w, h, 24 + k, 24, (255, 252, 220, 255))
        set_px(px, w, h, 24, 24 + k, (255, 252, 220, 255))


def main() -> None:
    make_goldie()
    make_glowfin()
    make_upgrade_icon("turbo.png", draw_turbo)
    make_upgrade_icon("diver.png", draw_diver)
    make_upgrade_icon("iron.png", draw_iron)
    make_upgrade_icon("magnet.png", draw_magnet)
    make_upgrade_icon("golden.png", draw_golden)
    print("Generated ModKit example art.")


if __name__ == "__main__":
    main()
