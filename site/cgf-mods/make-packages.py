#!/usr/bin/env python3
"""Deterministically build pure-source CGF mod packages and catalog.json for varialbe.xyz/cgf-mods."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import zipfile
from pathlib import Path

SITE_DIR = Path(__file__).resolve().parent
REPO_ROOT = SITE_DIR.parent.parent
SOURCES_DIR = SITE_DIR / "sources"
PACKAGES_DIR = SITE_DIR / "packages"
BASE_URL = "https://varialbe.xyz/cgf-mods/"
FIXED_ZIP_TIME = (2026, 11, 18, 0, 0, 0)
FORBIDDEN_EXTENSIONS = {".win", ".exe", ".dll", ".so", ".dylib"}


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
            "version": "2.3.0",
            "description": "Open the Missile Silo in NewWorld and fish its flooded launch shaft for four deep-water species.",
            "source_dir": REPO_ROOT / "examples" / "modkit" / "silo",
            "zip_name": "jamm-silo.zip",
            "categories": ["Optional", "Exploration"],
            "icon": "assets/silo.svg",
            "packageType": "modkit",
            "manifestFile": "jamm-mod.json",
            "isDefault": False,
        },
        {
            "id": "weather-pack",
            "name": "Weather Pack",
            "author": "JAMM",
            "version": "1.0.1",
            "description": "Four daylight weathers (Super Sunny, Rainy, Windy, Fog) with eight new surface fish, animated skies and original sound.",
            "source_dir": REPO_ROOT / "examples" / "modkit" / "weather-pack",
            "zip_name": "weather-pack.zip",
            "categories": ["Optional", "Weather"],
            "icon": "assets/weather.svg",
            "packageType": "modkit",
            "manifestFile": "jamm-mod.json",
            "isDefault": False,
        },
        {
            "id": "endless-ocean",
            "name": "Endless Ocean (Preview)",
            "author": "JAMM",
            "version": "0.2.0",
            "description": "A seeded seafloor with three biomes, vanilla fish and occasional wrecks east of NewWorld.",
            "source_dir": REPO_ROOT / "examples" / "modkit" / "endless-ocean",
            "zip_name": "endless-ocean.zip",
            "categories": ["Optional", "Exploration"],
            "icon": "assets/endless-ocean.svg",
            "packageType": "recipe",
            "manifestFile": "jamm-recipe.json",
            "isDefault": False,
        },
        {
            "id": "example-rod",
            "name": "Example Rod",
            "author": "JAMM",
            "version": "1.0.0",
            "description": "Test ModKit source package adding the Copper Current rod ($25,000) to the rod shop.",
            "source_dir": REPO_ROOT / "examples" / "modkit" / "example-rod",
            "zip_name": "example-rod.zip",
            "categories": ["ModKit", "Rods"],
            "icon": "assets/example-rod.svg",
            "packageType": "modkit",
            "manifestFile": "jamm-mod.json",
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
