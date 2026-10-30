#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
generate_pwa_icons.py — Sinh icon PWA cho Driver App (6C.7).
=============================================================
Khong can thu vien ngoai: tao file PNG truc tiep bang zlib + struct
(PNG 8-bit RGBA, vong bo ke 1 mau nen + hinh chu nhat + duong trang).

Muc dich:
  - `icons/icon-192.png`            : icon 192x192 (any)
  - `icons/icon-512.png`            : icon 512x512 (any)
  - `icons/icon-512-maskable.png`   : icon 512x512 (maskable, an toàn vung an)
  - `public/favicon.svg`            : favicon vector

Mau thuong hieu ERP FMCG: xanh navy #1E40AF, chu "E" trang.
"""
from __future__ import annotations

import struct
import sys
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent  # erp-fmcg-driver/
PUBLIC = ROOT / "public"
ICONS = PUBLIC / "icons"

BG = (0x1E, 0x40, 0xAF, 255)      # navy
FG = (0xFF, 0xFF, 0xFF, 255)      # white
ACCENT = (0xF5, 0x9E, 0x0B, 255)  # amber (vach accent)


def write_png(path: Path, width: int, height: int, pixels: list[list[tuple]]) -> None:
    """Ghi PNG 8-bit RGBA (khong can thu vien ngoai)."""
    raw = bytearray()
    for row in pixels:
        raw.append(0)  # filter type 0 (None)
        for (r, g, b, a) in row:
            raw += bytes((r, g, b, a))

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def draw_icon(size: int, maskable: bool = False) -> list[list[tuple]]:
    """Ve icon: nen navy, chu 'E' trang, vach amber, bo goc (neu maskable)."""
    px = [[BG for _ in range(size)] for _ in range(size)]

    # --- Vung an toan (safe zone) cho maskable: 80% giua a ---
    if maskable:
        inset = int(size * 0.10)
    else:
        inset = int(size * 0.08)

    # --- Vach accent phia duoi ---
    bar_h = max(2, size // 28)
    bar_y = int(size * 0.70)
    for y in range(bar_y, min(size, bar_y + bar_h)):
        for x in range(inset, size - inset):
            px[y][x] = ACCENT

    # --- Chu "E" (blocking) ---
    # Tham so trong khung chu [x0, x1] x [y0, y1]
    x0 = int(size * 0.30)
    x1 = int(size * 0.66)
    y0 = int(size * 0.24)
    y1 = int(size * 0.68)

    stem = max(3, (x1 - x0) // 5)          # be do day cua chu
    arm_h = max(3, (y1 - y0) // 7)          # be do day cac ngang
    mid_y = (y0 + y1) // 2

    def fill_rect(xa: int, xb: int, ya: int, yb: int, color) -> None:
        for y in range(max(0, ya), min(size, yb)):
            for x in range(max(0, xa), min(size, xb)):
                px[y][x] = color

    fill_rect(x0, x0 + stem, y0, y1, FG)                       # canh doc
    fill_rect(x0, x1, y0, y0 + arm_h, FG)                      # ngang tren
    fill_rect(x0, x1, mid_y, mid_y + arm_h, FG)                # nhang giua
    fill_rect(x0, x1, y1 - arm_h, y1, FG)                      # nhang duoi

    return px


FAVICON_SVG = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
  <rect width="100" height="100" rx="18" fill="#1E40AF"/>
  <rect x="30" y="24" width="36" height="44" fill="none"/>
  <path d="M30 24h36v9H39v11h24v9H39v11h27v9H30z" fill="#ffffff"/>
  <rect x="16" y="72" width="68" height="7" fill="#F59E0B"/>
</svg>
"""


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    targets = [
        (ICONS / "icon-192.png", 192, False),
        (ICONS / "icon-512.png", 512, False),
        (ICONS / "icon-512-maskable.png", 512, True),
    ]

    for path, size, maskable in targets:
        px = draw_icon(size, maskable)
        write_png(path, size, size, px)
        print(f"  [OK] {path.relative_to(ROOT)} ({size}x{size}, "
              f"{'maskable' if maskable else 'any'}) — {path.stat().st_size:,} bytes")

    fav = PUBLIC / "favicon.svg"
    fav.write_text(FAVICON_SVG, encoding="utf-8")
    print(f"  [OK] {fav.relative_to(ROOT)} — {fav.stat().st_size:,} bytes")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
