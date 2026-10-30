#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
07_unified_verify.py — BỘ KIỂM TRA DUY NHẤT cho ERP_FMCG.
==============================================================
★ PHASE 6C.3 — "Hợp nhất bộ verify"

Trước đây có 2 bộ verify cạnh tranh nhau:
  - `02_verify.sql`            (26 check, viết tay trong SQL)
  - `scripts/02_verify_data.py` (22 check, viết tay trong Python)
  -> Lệch nhau, thêm check phải sửa 2 nơi, dễ quên.

Sau Phase 6C.3:
  - `scripts/verify_registry.py`  = registry duy nhất (nguồn sự thật)
  - `scripts/07_unified_verify.py` = file này — chạy TẤT CẢ check
  - `scripts/build_verify_sql.py`  = sinh `02_verify.sql` từ registry
  - `scripts/02_verify_data.py`    = wrapper mỏng, gọi lại file này

Exit code:
  0 = tất cả PASS / INFO
  1 = có ít nhất 1 FAIL
  2 = lỗi kết nối / lỗi SQL

Usage:
    python scripts/07_unified_verify.py
    python scripts/07_unified_verify.py --json
    python scripts/07_unified_verify.py --nhom P6
    python scripts/07_unified_verify.py --output reports/
"""
from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path
from typing import Any

try:
    import pyodbc
except ImportError:  # pragma: no cover
    print("LOI: chua cai pyodbc. Chay: python -m pip install -r requirements.txt",
          file=sys.stderr)
    raise SystemExit(2)

sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_registry import ALL_CHECKS, Check, by_nhom, evaluate  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def connect(server: str, database: str) -> pyodbc.Connection:
    cs = (
        f"DRIVER={{SQL Server}};SERVER={server};"
        f"DATABASE={database};Trusted_Connection=yes;"
        "Encrypt=no;TrustServerCertificate=yes;"
    )
    return pyodbc.connect(cs, timeout=30)


def run_checks(conn: pyodbc.Connection, checks: list[Check]) -> list[dict[str, Any]]:
    cur = conn.cursor()
    results: list[dict[str, Any]] = []
    for c in checks:
        gia_tri: Any = None
        loi: str | None = None
        try:
            cur.execute(c.sql)
            row = cur.fetchone()
            gia_tri = row[0] if row else None
        except Exception as exc:  # noqa: BLE001
            loi = str(exc)
        if loi:
            trang_thai = "FAIL"
        else:
            trang_thai = evaluate(c, gia_tri)
        results.append({
            "ma": c.code,
            "nhom": c.nhom,
            "mo_ta": c.mo_ta,
            "gia_tri": gia_tri,
            "ky_vong": c.ky_vong,
            "kieu": c.kieu,
            "trang_thai": trang_thai,
            "loi": loi,
        })
    try:
        cur.close()
    except Exception:
        pass
    return results


def print_table(results: list[dict[str, Any]], server: str, database: str) -> None:
    print("=" * 100)
    print(f" ERP-FMCG - BO KIEM TRA THONG NHAT ({server} / {database})")
    print("=" * 100)
    print(f"{'Ma':<8}{'Mo ta':<58}{'Gia tri':>14}  {'Ky vong':<10} {'Trang thai':<10}")
    print("-" * 100)
    for r in results:
        val = r["gia_tri"]
        val_s = f"{val:,}" if isinstance(val, int) else str(val)
        print(f"{r['ma']:<8}{r['mo_ta'][:57]:<58}{val_s:>14}  "
              f"{r['ky_vong']:<10} {r['trang_thai']:<10}")
    print("-" * 100)
    n_pass = sum(1 for r in results if r["trang_thai"] == "PASS")
    n_fail = sum(1 for r in results if r["trang_thai"] == "FAIL")
    n_info = sum(1 for r in results if r["trang_thai"] == "INFO")
    print(f"  PASS: {n_pass}   FAIL: {n_fail}   INFO: {n_info}   TONG: {len(results)}")
    if n_fail:
        print("\n  CHECK FAIL:")
        for r in results:
            if r["trang_thai"] == "FAIL":
                extra = f" [{r['loi'][:60]}]" if r["loi"] else ""
                print(f"    - {r['ma']}: {r['mo_ta']} "
                      f"(gia tri={r['gia_tri']}, ky vong={r['ky_vong']}){extra}")
    print("=" * 100)


def write_markdown(path: Path, results: list[dict[str, Any]],
                   server: str, database: str) -> None:
    n_pass = sum(1 for r in results if r["trang_thai"] == "PASS")
    n_fail = sum(1 for r in results if r["trang_thai"] == "FAIL")
    n_info = sum(1 for r in results if r["trang_thai"] == "INFO")
    lines = [
        "# Bao cao kiem tra du lieu ERP-FMCG",
        "",
        f"- Server: `{server}`",
        f"- Database: `{database}`",
        f"- Tong so check: **{len(results)}**",
        f"- PASS: **{n_pass}** · FAIL: **{n_fail}** · INFO: **{n_info}**",
        "",
        "| Ma | Nhom | Mo ta | Gia tri | Ky vong | Trang thai |",
        "|---|---|---|---:|---|---|",
    ]
    for r in results:
        val = r["gia_tri"]
        val_s = f"{val:,}" if isinstance(val, int) else str(val)
        lines.append(
            f"| {r['ma']} | {r['nhom']} | {r['mo_ta']} | {val_s} | "
            f"{r['ky_vong']} | {r['trang_thai']} |"
        )
    lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser(
        description="ERP-FMCG - bo kiem tra du lieu thong nhat (Phase 6C.3)")
    ap.add_argument("--server", default=r".\SQLEXPRESS")
    ap.add_argument("--database", default="ERP_FMCG")
    ap.add_argument("--output", default=None,
                    help="Thu muc xuat CSV/JSON/Markdown (vd: reports/)")
    ap.add_argument("--json", action="store_true",
                    help="In ket qua ra stdout duy nhat dang JSON")
    ap.add_argument("--nhom", default=None,
                    help="Chi chay 1 nhom check (A | D | P6)")
    args = ap.parse_args()

    checks = by_nhom(args.nhom) if args.nhom else ALL_CHECKS
    if not checks:
        print(f"LOI: nhom '{args.nhom}' khong ton tai", file=sys.stderr)
        return 2

    try:
        conn = connect(args.server, args.database)
    except Exception as exc:
        print(f"LOI ket noi '{args.server}/{args.database}': {exc}", file=sys.stderr)
        return 2

    try:
        results = run_checks(conn, checks)
    finally:
        conn.close()

    n_fail = sum(1 for r in results if r["trang_thai"] == "FAIL")

    if args.json:
        print(json.dumps({
            "server": args.server, "database": args.database,
            "pass": sum(1 for r in results if r["trang_thai"] == "PASS"),
            "fail": n_fail,
            "info": sum(1 for r in results if r["trang_thai"] == "INFO"),
            "results": results,
        }, ensure_ascii=False, indent=2))
    else:
        print_table(results, args.server, args.database)

    if args.output:
        out_dir = Path(args.output)
        out_dir.mkdir(parents=True, exist_ok=True)

        csv_path = out_dir / "verify_report.csv"
        with csv_path.open("w", newline="", encoding="utf-8-sig") as fh:
            w = csv.DictWriter(
                fh,
                fieldnames=["ma", "nhom", "mo_ta", "gia_tri", "ky_vong",
                            "kieu", "trang_thai", "loi"],
                extrasaction="ignore",
            )
            w.writeheader()
            w.writerows(results)

        json_path = out_dir / "verify_report.json"
        json_path.write_text(
            json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")

        md_path = out_dir / "verify_report.md"
        write_markdown(md_path, results, args.server, args.database)

        if not args.json:
            print(f"\n  Da xuat: {csv_path}")
            print(f"  Da xuat: {json_path}")
            print(f"  Da xuat: {md_path}")

    return 1 if n_fail else 0


if __name__ == "__main__":
    raise SystemExit(main())
