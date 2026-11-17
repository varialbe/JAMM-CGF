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


def fill_rect(px, w, h, x0, y0, x1, y1, col):
    for y in range(max(0, int(y0)), min(h, int(y1) + 1)):
        for x in range(max(0, int(x0)), min(w, int(x1) + 1)):
            set_px(px, w, h, x, y, col)


def fill_ellipse(px, w, h, cx, cy, rx, ry, col):
    for y in range(max(0, int(cy - ry - 1)), min(h, int(cy + ry + 2))):
        for x in range(max(0, int(cx - rx - 1)), min(w, int(cx + rx + 2))):
            dx = (x - cx) / rx
            dy = (y - cy) / ry
            if dx * dx + dy * dy <= 1.0:
                set_px(px, w, h, x, y, col)


def draw_thick_line(px, w, h, x0, y0, x1, y1, radius, col):
    steps = max(1, int(math.hypot(x1 - x0, y1 - y0) * 2))
    for i in range(steps + 1):
        t = i / steps
        cx = x0 + (x1 - x0) * t
        cy = y0 + (y1 - y0) * t
        fill_ellipse(px, w, h, cx, cy, radius, radius, col)


def draw_paddle_raft(px, w, h, s: int) -> None:
    # s=1 -> 100x40 card, s=2 -> 200x80 world sprite (deck centered around y = h // 2)
    def sc(v: float) -> float:
        return v * s

    # Small pennant mast on stern (left)
    fill_rect(px, w, h, sc(20), sc(7), sc(22), sc(21), (135, 86, 44, 255))
    for i in range(int(sc(11))):
        t = i / max(1, sc(11))
        top = sc(8 + 2.5 * t)
        bot = sc(14 - 2.5 * t)
        fill_rect(px, w, h, sc(22) + i, top, sc(22) + i, bot, (235, 82, 68, 255))
    fill_rect(px, w, h, sc(22), sc(10), sc(25), sc(11), (255, 220, 110, 255))

    # Wooden crate / seat on deck
    fill_rect(px, w, h, sc(32), sc(14), sc(48), sc(21), (195, 138, 72, 255))
    fill_rect(px, w, h, sc(33), sc(14), sc(47), sc(16), (225, 172, 102, 255))
    fill_rect(px, w, h, sc(32), sc(17), sc(48), sc(18), (150, 96, 44, 255))

    # Main lashed-log deck + hull (top deck sits right around y = 20*s = h//2)
    fill_rect(px, w, h, sc(14), sc(20), sc(85), sc(29), (168, 108, 52, 255))
    fill_rect(px, w, h, sc(15), sc(20), sc(84), sc(22), (212, 152, 86, 255))
    fill_rect(px, w, h, sc(16), sc(27), sc(83), sc(29), (126, 76, 36, 255))

    # Rounded log cross-sections along the side
    for lx in range(18, 86, 11):
        fill_ellipse(px, w, h, sc(lx), sc(25), sc(5.0), sc(4.4), (178, 118, 58, 255))
        fill_ellipse(px, w, h, sc(lx), sc(25), sc(3.2), sc(2.6), (222, 172, 112, 255))
        fill_ellipse(px, w, h, sc(lx), sc(25), sc(1.4), sc(1.2), (165, 108, 54, 255))

    # Rope lashings binding the logs
    for rx in (27, 68):
        fill_rect(px, w, h, sc(rx), sc(19), sc(rx + 3), sc(29), (235, 212, 155, 255))
        fill_rect(px, w, h, sc(rx + 1), sc(20), sc(rx + 2), sc(28), (196, 168, 110, 255))

    # Wooden paddle angled off the bow/side
    draw_thick_line(px, w, h, sc(54), sc(15), sc(78), sc(30), sc(1.2), (186, 128, 64, 255))
    fill_ellipse(px, w, h, sc(80), sc(31), sc(5.5), sc(2.6), (222, 164, 92, 255))
    fill_ellipse(px, w, h, sc(80), sc(30.5), sc(3.6), sc(1.3), (242, 198, 130, 255))


def make_paddle_raft() -> None:
    w, h = 200, 80
    px = blank(w, h)
    draw_paddle_raft(px, w, h, s=2)
    add_outline(px, w, h, (58, 34, 18, 255))
    write_png(os.path.join(ROOT, "harbor-pack", "art", "paddle_raft.png"), w, h, px)

    cw, ch = 100, 40
    cpx = blank(cw, ch)
    draw_paddle_raft(cpx, cw, ch, s=1)
    add_outline(cpx, cw, ch, (58, 34, 18, 255))
    write_png(os.path.join(ROOT, "harbor-pack", "art", "paddle_raft_card.png"), cw, ch, cpx)


def draw_comet_cruiser(px, w, h, s: float) -> None:
    # Base coordinate space: 120 x 48 (deck centered around y = 24)
    def sc(v: float) -> float:
        return v * s

    # Cabin & tinted cyan windshield above deck (y = 11..24)
    for y in range(int(sc(12)), int(sc(25))):
        for x in range(int(sc(34)), int(sc(76))):
            ny = y / s
            nx = x / s
            right_edge = 66 + (ny - 12) * 0.9
            left_edge = 36 - (ny - 12) * 0.2
            if left_edge <= nx <= right_edge:
                if ny >= 14 and nx >= 55:
                    col = (105, 220, 248, 255) if ny < 19 else (68, 175, 215, 255)
                elif ny < 14:
                    col = (242, 248, 255, 255)
                else:
                    col = (220, 232, 246, 255)
                set_px(px, w, h, x, y, col)

    # Windshield specular highlight
    draw_thick_line(px, w, h, sc(59), sc(15), sc(65), sc(21), sc(0.9), (210, 252, 255, 255))

    # Radar / radio antenna on cabin roof
    fill_rect(px, w, h, sc(42), sc(7), sc(44), sc(12), (185, 200, 220, 255))
    fill_ellipse(px, w, h, sc(43), sc(7), sc(2.2), sc(1.6), (255, 195, 60, 255))

    # Outboard thruster motor at stern (left)
    fill_rect(px, w, h, sc(11), sc(21), sc(20), sc(32), (62, 76, 98, 255))
    fill_rect(px, w, h, sc(12), sc(21), sc(19), sc(24), (245, 110, 55, 255))
    fill_rect(px, w, h, sc(13), sc(31), sc(17), sc(36), (130, 148, 170, 255))

    # Sleek speedboat hull (y = 23..36, sharp tapered bow on right)
    for y in range(int(sc(23)), int(sc(37))):
        for x in range(int(sc(18)), int(sc(112))):
            ny = y / s
            nx = x / s
            bow_limit = 110 - (ny - 23) * 1.55
            stern_limit = 19 + max(0.0, (ny - 30) * 0.8)
            if stern_limit <= nx <= bow_limit:
                if ny < 25.5:
                    col = (248, 252, 255, 255)  # crisp deck highlight
                elif 27.0 <= ny <= 29.2:
                    col = (250, 108, 52, 255)   # coral comet racing stripe
                elif 29.2 < ny <= 30.8:
                    col = (255, 208, 68, 255)   # gold accent stripe
                elif ny < 32.5:
                    col = (228, 238, 250, 255)  # white upper hull
                else:
                    col = (34, 72, 132, 255)    # deep navy lower hull
                set_px(px, w, h, x, y, col)

    # Golden comet star emblem near bow
    fill_ellipse(px, w, h, sc(89), sc(28.5), sc(2.4), sc(2.0), (255, 232, 110, 255))
    fill_rect(px, w, h, sc(83), sc(28), sc(88), sc(29), (255, 232, 110, 255))


def make_comet_cruiser() -> None:
    w, h = 240, 96
    px = blank(w, h)
    draw_comet_cruiser(px, w, h, s=2.0)
    add_outline(px, w, h, (16, 26, 48, 255))
    write_png(os.path.join(ROOT, "harbor-pack", "art", "comet_cruiser.png"), w, h, px)

    cw, ch = 120, 48
    cpx = blank(cw, ch)
    draw_comet_cruiser(cpx, cw, ch, s=1.0)
    add_outline(cpx, cw, ch, (16, 26, 48, 255))
    write_png(os.path.join(ROOT, "harbor-pack", "art", "comet_cruiser_card.png"), cw, ch, cpx)


def make_fishers_beanie() -> None:
    # 96x76 (renders at 0.25x = 24x19)
    w, h = 96, 76
    px = blank(w, h)
    cx = 48.0

    # Fluffy cream pom-pom on top
    fill_ellipse(px, w, h, cx, 15.0, 10.5, 8.5, (248, 238, 210, 255))
    fill_ellipse(px, w, h, cx - 2.5, 13.0, 6.0, 4.5, (255, 252, 240, 255))

    # Cozy teal knit dome with vertical rib texture
    for y in range(18, 48):
        for x in range(16, 80):
            dx = (x - cx) / 28.0
            dy = (y - 47.0) / 27.0
            if dx * dx + dy * dy <= 1.0:
                rib = ((x - 16) // 6) % 2 == 0
                if y < 27:
                    col = (84, 178, 192, 255) if rib else (66, 156, 174, 255)
                else:
                    col = (54, 142, 162, 255) if rib else (42, 120, 140, 255)
                set_px(px, w, h, x, y, col)

    # Folded warm mustard-gold ribbed cuff
    for y in range(44, 64):
        for x in range(14, 82):
            dx = (x - cx) / 33.5
            dy = (y - 54.0) / 10.5
            if abs(dx) <= 1.0 and (abs(dx) < 0.85 or dy * dy <= 1.0):
                rib = ((x - 14) // 5) % 2 == 0
                if y < 48:
                    col = (250, 206, 92, 255) if rib else (232, 184, 68, 255)
                elif y > 59:
                    col = (196, 140, 42, 255)
                else:
                    col = (238, 186, 66, 255) if rib else (216, 162, 50, 255)
                set_px(px, w, h, x, y, col)

    add_outline(px, w, h, (20, 32, 48, 255))
    write_png(os.path.join(ROOT, "harbor-pack", "art", "fishers_beanie.png"), w, h, px)


def make_lucky_captain_hat() -> None:
    # 104x80 (renders at 0.25x = 26x20)
    w, h = 104, 80
    px = blank(w, h)
    cx = 50.0

    # Crisp white captain's cap crown flaring wider at the top
    for y in range(14, 45):
        t = (y - 14) / 31.0
        half_w = 34.0 - t * 6.0
        for x in range(12, 92):
            top_y = 14.0 + (((x - cx) / 36.0) ** 2) * 5.0
            if y >= top_y and abs(x - cx) <= half_w:
                if y < 22:
                    col = (252, 254, 255, 255)
                elif y > 37:
                    col = (212, 224, 238, 255)
                else:
                    col = (236, 244, 252, 255)
                set_px(px, w, h, x, y, col)

    # Navy band with gold braided trim
    fill_rect(px, w, h, 22, 41, 78, 52, (26, 48, 88, 255))
    fill_rect(px, w, h, 22, 41, 78, 43, (245, 202, 68, 255))
    fill_rect(px, w, h, 22, 50, 78, 52, (224, 174, 44, 255))

    # Glossy navy brim / visor extending toward the right (cat faces right)
    for y in range(51, 66):
        t = (y - 51) / 14.0
        left_x = 34 + t * 14
        right_x = 92 - (t ** 1.6) * 14
        for x in range(int(left_x), int(right_x) + 1):
            col = (44, 78, 134, 255) if y < 56 else (22, 40, 76, 255)
            set_px(px, w, h, x, y, col)
    draw_thick_line(px, w, h, 52, 55, 82, 57, 1.4, (86, 132, 198, 255))

    # Lucky four-leaf emerald clover + gold badge on front of crown
    fill_ellipse(px, w, h, cx, 29.0, 9.0, 8.5, (240, 192, 56, 255))
    fill_ellipse(px, w, h, cx, 29.0, 6.8, 6.4, (30, 56, 98, 255))
    for lx, ly in ((-2.6, -2.4), (2.6, -2.4), (-2.6, 2.2), (2.6, 2.2)):
        fill_ellipse(px, w, h, cx + lx, 28.5 + ly, 2.7, 2.5, (72, 212, 112, 255))
    fill_rect(px, w, h, cx - 0.5, 30, cx + 0.5, 34, (52, 178, 86, 255))

    add_outline(px, w, h, (14, 22, 40, 255))
    write_png(os.path.join(ROOT, "harbor-pack", "art", "lucky_captain_hat.png"), w, h, px)


def main() -> None:
    make_goldie()
    make_glowfin()
    make_upgrade_icon("turbo.png", draw_turbo)
    make_upgrade_icon("diver.png", draw_diver)
    make_upgrade_icon("iron.png", draw_iron)
    make_upgrade_icon("magnet.png", draw_magnet)
    make_upgrade_icon("golden.png", draw_golden)
    make_paddle_raft()
    make_comet_cruiser()
    make_fishers_beanie()
    make_lucky_captain_hat()
    print("Generated ModKit example art.")


if __name__ == "__main__":
    main()
