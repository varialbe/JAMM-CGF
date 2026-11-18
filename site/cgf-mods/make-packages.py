#!/usr/bin/env python3
"""Deterministically build pure-source CGF mod packages and catalog.json for varialbe.xyz/cgf-mods."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import struct
import zipfile
import zlib
from pathlib import Path

SITE_DIR = Path(__file__).resolve().parent
REPO_ROOT = SITE_DIR.parent.parent
SOURCES_DIR = SITE_DIR / "sources"
PACKAGES_DIR = SITE_DIR / "packages"
BASE_URL = "https://varialbe.xyz/cgf-mods/"
FIXED_ZIP_TIME = (2026, 11, 18, 0, 0, 0)
FORBIDDEN_EXTENSIONS = {".win", ".exe", ".dll", ".so", ".dylib"}


def write_png(path: Path, width: int, height: int, rgba: bytearray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    raw = bytearray()
    row_bytes = width * 4
    for y in range(height):
        raw.append(0)
        start = y * row_bytes
        raw.extend(rgba[start : start + row_bytes])

    def chunk(tag: bytes, data: bytes) -> bytes:
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    idat = zlib.compress(bytes(raw), level=9)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", idat) + chunk(b"IEND", b""))


def fill_ellipse(buf: bytearray, w: int, h: int, cx: float, cy: float, rx: float, ry: float, col: tuple[int, int, int, int]) -> None:
    for y in range(max(0, int(cy - ry - 1)), min(h, int(cy + ry + 2))):
        for x in range(max(0, int(cx - rx - 1)), min(w, int(cx + rx + 2))):
            if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0:
                idx = (y * w + x) * 4
                buf[idx : idx + 4] = bytes(col)


def fill_triangle(buf: bytearray, w: int, h: int, p1: tuple[int, int], p2: tuple[int, int], p3: tuple[int, int], col: tuple[int, int, int, int]) -> None:
    x1, y1 = p1
    x2, y2 = p2
    x3, y3 = p3
    for y in range(max(0, min(y1, y2, y3)), min(h, max(y1, y2, y3) + 1)):
        for x in range(max(0, min(x1, x2, x3)), min(w, max(x1, x2, x3) + 1)):
            c1 = (x2 - x1) * (y - y1) - (y2 - y1) * (x - x1)
            c2 = (x3 - x2) * (y - y2) - (y3 - y2) * (x - x2)
            c3 = (x1 - x3) * (y - y3) - (y1 - y3) * (x - x3)
            if (c1 >= 0 and c2 >= 0 and c3 >= 0) or (c1 <= 0 and c2 <= 0 and c3 <= 0):
                idx = (y * w + x) * 4
                buf[idx : idx + 4] = bytes(col)


def generate_silo_art() -> None:
    art_dir = SOURCES_DIR / "jamm-silo" / "art"
    # 1. Soft radial light glow (128x128)
    size = 128
    half = size * 0.5
    glow = bytearray(size * size * 4)
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - half, y - half) / half
            falloff = max(0.0, min(1.0, 1.0 - d))
            a = round(falloff * falloff * 255.0)
            idx = (y * size + x) * 4
            glow[idx : idx + 4] = bytes((255, 255, 255, a))
    write_png(art_dir / "spr_jamm_silo_glow.png", size, size, glow)

    # 2. Original cozy cat diver strip (128x48, 2 frames)
    fw, h, frames = 64, 48, 2
    w = fw * frames
    diver = bytearray(w * h * 4)
    for f in range(frames):
        ox = f * fw
        flipper = -2 if f == 0 else 2
        fill_ellipse(diver, w, h, ox + 28, 26, 14, 10, (45, 165, 175, 255))
        fill_ellipse(diver, w, h, ox + 40, 22, 11, 11, (240, 185, 70, 255))
        fill_ellipse(diver, w, h, ox + 42, 22, 7, 7, (155, 235, 255, 255))
        fill_triangle(diver, w, h, (ox + 36, 14), (ox + 39, 8), (ox + 42, 14), (240, 185, 70, 255))
        fill_triangle(diver, w, h, (ox + 43, 14), (ox + 46, 8), (ox + 49, 14), (240, 185, 70, 255))
        fill_ellipse(diver, w, h, ox + 12, 24 + flipper, 7, 4, (245, 140, 55, 255))
        fill_ellipse(diver, w, h, ox + 13, 30 - flipper, 7, 4, (245, 140, 55, 255))
    write_png(art_dir / "spr_jamm_silo_diver.png", w, h, diver)

    # 3. Original sanctuary lantern fish strip (96x32, 2 frames)
    fw, h, frames = 48, 32, 2
    w = fw * frames
    fish = bytearray(w * h * 4)
    for f in range(frames):
        ox = f * fw
        tail = 0 if f == 0 else 2
        fill_ellipse(fish, w, h, ox + 21, 16, 13, 8, (255, 255, 255, 255))
        fill_triangle(fish, w, h, (ox + 32, 16), (ox + 44, 8 + tail), (ox + 44, 24 - tail), (225, 245, 255, 255))
        fill_ellipse(fish, w, h, ox + 13, 14, 2, 2, (25, 35, 50, 255))
    write_png(art_dir / "spr_jamm_silo_fish.png", w, h, fish)


def build_zip(source_dir: Path, zip_path: Path) -> tuple[str, int]:
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    files = sorted(p for p in source_dir.rglob("*") if p.is_file())
    if not files:
        raise RuntimeError(f"No source files found in {source_dir}")
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for file_path in files:
            if file_path.is_symlink():
                raise RuntimeError(f"Symlink forbidden in package: {file_path}")
            if file_path.suffix.lower() in FORBIDDEN_EXTENSIONS:
                raise RuntimeError(f"Proprietary/binary extension forbidden in package: {file_path}")
            rel = file_path.relative_to(source_dir).as_posix()
            info = zipfile.ZipInfo(rel, date_time=FIXED_ZIP_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            zf.writestr(info, file_path.read_bytes())
    raw = zip_path.read_bytes()
    return hashlib.sha256(raw).hexdigest(), len(raw)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sync-inputs", action="store_true", help="Also write ../inputs/catalog.json")
    args = parser.parse_args()

    generate_silo_art()

    specs = [
        {
            "id": "builtin-perf",
            "name": "JAMM Perf",
            "author": "JAMM",
            "version": "1.0.0",
            "description": "First-party default performance optimizations and optional fish scheduling (F8 toggles modes).",
            "source_dir": SOURCES_DIR / "jamm-perf",
            "zip_name": "jamm-perf.zip",
            "categories": ["First-Party Default", "Performance"],
            "icon": "assets/perf.svg",
            "packageType": "recipe",
            "manifestFile": "jamm-recipe.json",
            "isDefault": True,
        },
        {
            "id": "builtin-smooth",
            "name": "JAMM Smooth",
            "author": "JAMM",
            "version": "1.0.0",
            "description": "First-party default 60/120 FPS interpolated rendering while preserving 30 ticks/s simulation (F7 cycles).",
            "source_dir": SOURCES_DIR / "jamm-smooth",
            "zip_name": "jamm-smooth.zip",
            "categories": ["First-Party Default", "Visuals"],
            "icon": "assets/smooth.svg",
            "packageType": "recipe",
            "manifestFile": "jamm-recipe.json",
            "isDefault": True,
        },
        {
            "id": "builtin-hd-lite",
            "name": "JAMM HD Lite",
            "author": "JAMM",
            "version": "1.0.0",
            "description": "First-party default aspect-correct fullscreen rendering with original texture pages (F11 toggles).",
            "source_dir": SOURCES_DIR / "jamm-hd-lite",
            "zip_name": "jamm-hd-lite.zip",
            "categories": ["First-Party Default", "Display"],
            "icon": "assets/hd-lite.svg",
            "packageType": "recipe",
            "manifestFile": "jamm-recipe.json",
            "isDefault": True,
        },
        {
            "id": "builtin-console",
            "name": "JAMM Console",
            "author": "JAMM",
            "version": "1.0.0",
            "description": "First-party default optional in-game command console (F12 or ` opens; type help for commands).",
            "source_dir": SOURCES_DIR / "jamm-console",
            "zip_name": "jamm-console.zip",
            "categories": ["First-Party Default", "Utility"],
            "icon": "assets/console.svg",
            "packageType": "recipe",
            "manifestFile": "jamm-recipe.json",
            "isDefault": True,
        },
        {
            "id": "example-fish",
            "name": "Example Fish",
            "author": "JAMM",
            "version": "1.0.0",
            "description": "Test ModKit source package adding two original custom fish: Goldie (shallow) and Glowfin (nocturnal).",
            "source_dir": REPO_ROOT / "examples" / "modkit" / "example-fish",
            "zip_name": "example-fish.zip",
            "categories": ["ModKit", "Fish"],
            "icon": "assets/example-fish.svg",
            "packageType": "modkit",
            "manifestFile": "jamm-mod.json",
            "isDefault": False,
        },
        {
            "id": "jamm-silo",
            "name": "The Silo",
            "author": "JAMM",
            "version": "1.0.0",
            "description": "Non-default optional isolated mod. Activates the Missile Silo hatch with animation, lets you descend into a calm sanctuary, and exit safely.",
            "source_dir": SOURCES_DIR / "jamm-silo",
            "zip_name": "jamm-silo.zip",
            "categories": ["Optional", "Exploration"],
            "icon": "assets/silo.svg",
            "packageType": "recipe",
            "manifestFile": "jamm-recipe.json",
            "isDefault": False,
        },
    ]

    catalog = []
    for spec in specs:
        zip_path = PACKAGES_DIR / spec["zip_name"]
        sha256, size_bytes = build_zip(spec["source_dir"], zip_path)
        catalog.append(
            {
                "id": spec["id"],
                "name": spec["name"],
                "author": spec["author"],
                "description": spec["description"],
                "fileUrl": f"{BASE_URL}packages/{spec['zip_name']}",
                "isPreview": False,
                "categories": spec["categories"],
                "createdUtc": "2026-11-18T00:00:00Z",
                "rating": None,
                "iconUrl": f"{BASE_URL}{spec['icon']}",
                "verifiedCreator": True,
                "credits": "JAMM-CGF first-party pure-source package",
                "catalogPage": f"{BASE_URL}#{spec['id']}",
                "packageType": spec["packageType"],
                "manifestFile": spec["manifestFile"],
                "isDefault": spec["isDefault"],
                "version": spec["version"],
                "sha256": sha256,
                "sizeBytes": size_bytes,
            }
        )
        print(f"{spec['zip_name']:20s} {size_bytes:6d} B  {sha256}")

    catalog_text = json.dumps(catalog, indent=2) + "\n"
    (SITE_DIR / "catalog.json").write_text(catalog_text, encoding="utf-8")
    if args.sync_inputs:
        inputs_catalog = REPO_ROOT.parent / "inputs" / "catalog.json"
        inputs_catalog.write_text(catalog_text, encoding="utf-8")
        print(f"Synced {inputs_catalog}")


if __name__ == "__main__":
    main()
