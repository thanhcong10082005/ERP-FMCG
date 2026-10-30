#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
02_verify_data.py — WRAPPER MỎNG quanh bộ verify thống nhất.

★ PHASE 6C.3 — "Hợp nhất bộ verify"
───────────────────────────────────────────────────────────────────────────
TRƯỚC Phase 6C.3:
  File này tự chứa 22 check viết tay -> trùng lặp với `02_verify.sql`
  (26 check viết tay) -> 2 nguồn kiểm tra lệch nhau.

SAU Phase 6C.3:
  - `scripts/verify_registry.py`  = registry DUY NHẤT (nguồn sự thật)
  - File này                    = wrapper, giữ nguyên CLI cũ để không
                                    phá script bàn giao / CI cũ.
  - `scripts/07_unified_verify.py` = bộ verify đầy đủ (A + D + P6).

Tương thích ngược: các tham số `--server`, `--database`, `--output`,
`--json` vẫn hoạt động y như cũ.

Exit code:
    0 = tất cả kiểm tra PASS (hoặc INFO)
    1 = có ít nhất 1 kiểm tra FAIL
    2 = lỗi kết nối / lỗi SQL
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
# Re-export để các script cũ import trực tiếp từ module này vẫn chạy.
from verify_registry import ALL_CHECKS, CHECKS_A, Check, evaluate  # noqa: E402,F401

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# Lưu ý: import trễ để tránh circular import với 07_unified_verify.
sys.path.insert(0, str(Path(__file__).resolve().parent))


def main() -> int:
    ap = argparse.ArgumentParser(
        description="ERP-FMCG - doi soat du lieu (wrapper cua bo verify thong nhat)")
    ap.add_argument("--server", default=r".\SQLEXPRESS")
    ap.add_argument("--database", default="ERP_FMCG")
    ap.add_argument("--output", default=None,
                    help="Thu muc xuat CSV/JSON/Markdown (vd: reports/)")
    ap.add_argument("--json", action="store_true",
                    help="In ket qua ra stdout duy nhat dang JSON")
    ap.add_argument("--nhom", default=None,
                    help="Chi chay 1 nhom check (A | D | P6)")
    args = ap.parse_args()

    # Chuyển toàn bộ việc thực thi sang bộ verify thống nhất.
    from importlib import import_module
    unified = import_module("07_unified_verify")

    argv = ["--server", args.server, "--database", args.database]
    if args.output:
        argv += ["--output", args.output]
    if args.json:
        argv.append("--json")
    if args.nhom:
        argv += ["--nhom", args.nhom]

    sys.argv = [sys.argv[0]] + argv
    return unified.main()


if __name__ == "__main__":
    raise SystemExit(main())
