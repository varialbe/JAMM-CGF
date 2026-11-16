#!/usr/bin/env python3
"""Upscale exported CGF texture pages 2x with Real-ESRGAN (anime model), keeping clean alpha edges.

usage: upscale_pages.py <pages dir from `jamm-modmaker export-pages`> <out dir> <realesrgan-ncnn-vulkan dir>

For every page marked "Upscale" in pages.json:
  1. colours are bled into fully transparent pixels (avoids dark fringes around sprites),
  2. colour and alpha are upscaled 2x separately with realesr-animevideov3 (cartoon model),
  3. both are recombined into page_N.png (2x size).
"""
import json
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image


def bleed(rgba: np.ndarray, passes: int = 12) -> np.ndarray:
    """Spread the colour of visible pixels into neighbouring transparent pixels."""
    rgb = rgba[..., :3].astype(np.float32)
    known = rgba[..., 3] > 0
    for _ in range(passes):
        if known.all():
            break
        acc = np.zeros_like(rgb)
        cnt = np.zeros(known.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            k = np.roll(known, (dy, dx), (0, 1))
            acc += np.roll(rgb, (dy, dx), (0, 1)) * k[..., None]
            cnt += k
        grow = (~known) & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][:, None]
        known = known | grow
    return rgb.clip(0, 255).astype(np.uint8)


def esrgan(tool: Path, src: Path, dst: Path) -> None:
    subprocess.run([str(tool / "realesrgan-ncnn-vulkan"), "-i", str(src), "-o", str(dst),
                    "-n", "realesr-animevideov3", "-s", "2", "-m", str(tool / "models")],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def main() -> int:
    pages, out, tool = (Path(a) for a in sys.argv[1:4])
    out.mkdir(parents=True, exist_ok=True)
    info = json.loads((pages / "pages.json").read_text())
    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        for page in info:
            if not page["Upscale"]:
                continue
            i = page["Index"]
            target = out / f"page_{i}.png"
            if target.exists():
                continue
            rgba = np.asarray(Image.open(pages / f"page_{i}.png").convert("RGBA"))
            h, w = rgba.shape[:2]
            Image.fromarray(bleed(rgba)).save(tmp / "rgb.png")
            Image.fromarray(np.repeat(rgba[..., 3:4], 3, axis=2)).save(tmp / "a.png")
            esrgan(tool, tmp / "rgb.png", tmp / "rgb2.png")
            esrgan(tool, tmp / "a.png", tmp / "a2.png")
            size = (w * 2, h * 2)
            rgb2 = Image.open(tmp / "rgb2.png").convert("RGB")
            a2 = Image.open(tmp / "a2.png").convert("L")
            if rgb2.size != size or a2.size != size:
                raise SystemExit(f"page {i}: upscaler returned {rgb2.size}, expected {size}")
            alpha = np.asarray(a2).astype(np.int16)
            alpha[alpha < 3] = 0          # keep empty space exactly empty
            alpha[alpha > 252] = 255
            result = np.dstack([np.asarray(rgb2), alpha.clip(0, 255).astype(np.uint8)])
            Image.fromarray(result, "RGBA").save(target, optimize=False, compress_level=6)
            print(f"page {i}: {w}x{h} -> {size[0]}x{size[1]}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
