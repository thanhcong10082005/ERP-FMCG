#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_verify_sql.py — SINH `02_verify.sql` từ `verify_registry.py`.
=============================================================
"Hợp nhất bộ verify"

Registry Python là nguồn sự thật duy nhất. File SQL được SINH RA
từ registry để:
  - `sqlcmd -i 02_verify.sql` vẫn chạy được (tương thích ngược)
  - Nhưng SQL KHÔNG BAO GIỜ lệch khỏi Python nữa.

Quy trình CI:
  1. `python scripts/build_verify_sql.py`   (sinh lại SQL)
  2. `git diff --exit-code 02_verify.sql`   (fail nếu SQL lệch -> buộc commit)
  3. `python scripts/07_unified_verify.py`  (chạy check thật)

Usage:
    python scripts/build_verify_sql.py
    python scripts/build_verify_sql.py --check   # chi kiem tra, khong ghi
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_registry import ALL_CHECKS, Check  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

PACK_ROOT = Path(__file__).resolve().parent.parent
OUT = PACK_ROOT / "02_verify.sql"

HEADER = """-- ===================================================================
-- ERP-FMCG - KIEM TRA DU LIEU SAU IMPORT (SINH TU verify_registry.py)
-- -------------------------------------------------------------------
-- *** FILE NAY DUOC SINH TU DONG - KHONG SUA TRUC TIEP ***
-- Nguon su that: scripts/verify_registry.py
-- Sinh lai:      python scripts/build_verify_sql.py
-- Chay:          sqlcmd -S ".\\SQLEXPRESS" -d "ERP_FMCG" -E -f 65001 -i 02_verify.sql
-- LUU Y:        -f 65001 BAT BUOC (file UTF-8 khong BOM)
--               Script KHONG co side-effect (chi SELECT + PRINT)
-- ===================================================================

SET NOCOUNT ON;
GO

PRINT '===================================================================';
PRINT ' ERP-FMCG - BAO CAO KIEM TRA DU LIEU (BO THONG NHAT)';
PRINT '===================================================================';
GO

"""


def sql_literal(s: str) -> str:
    return "'" + s.replace("'", "''") + "'"


def build_check_block(c: Check, order: int) -> str:
    """Sinh 1 dong UNION ALL cho bang tam checks."""
    ky = c.ky_vong
    # Ky vong dang so -> ep sang chuoi de so sanh nhu trong SQL
    ky_sql = f"'{ky}'"
    return (
        f"    SELECT {order} AS ord, {sql_literal(c.code)} AS ma, "
        f"{sql_literal(c.nhom)} AS nhom, {sql_literal(c.mo_ta)} AS kiem_tra,\n"
        f"           CAST((\n"
        f"{_indent(c.sql, 13)}\n"
        f"           ) AS nvarchar(50)) AS gia_tri,\n"
        f"           {ky_sql} AS ky_vong,\n"
        f"           {sql_literal(c.kieu)} AS kieu\n"
        f"    UNION ALL\n"
    )


def _indent(sql: str, n: int) -> str:
    pad = " " * n
    return "\n".join(pad + line.strip() for line in sql.strip().splitlines())


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="Chi kiem tra file da co dung khong, khong ghi lai")
    args = ap.parse_args()

    parts = [HEADER]
    parts.append("-- -------------------------------------------------------------------\n")
    parts.append("-- 1. BANG TAM TONG HOP CHECK (sinh tu registry)\n")
    parts.append("-- -------------------------------------------------------------------\n")
    parts.append("WITH checks AS (\n")

    blocks = []
    for i, c in enumerate(ALL_CHECKS):
        b = build_check_block(c, i + 1)
        if i == len(ALL_CHECKS) - 1:
            b = b.rstrip("\n").rstrip()  # bo "UNION ALL" cuoi
            b = b[: b.rfind("UNION ALL")].rstrip() + "\n"
        blocks.append(b)
    parts.append("".join(blocks))
    parts.append(")\n")
    parts.append(
        "SELECT ma, nhom, kiem_tra, gia_tri, ky_vong,\n"
        "       CASE\n"
        "           WHEN kieu = 'info' THEN 'INFO'\n"
        "           WHEN kieu = 'eq' AND CAST(gia_tri AS bigint) = CAST(ky_vong AS bigint) THEN 'PASS'\n"
        "           WHEN kieu = 'ge' AND CAST(gia_tri AS bigint) >= CAST(ky_vong AS bigint) THEN 'PASS'\n"
        "           WHEN kieu = 'gt' AND CAST(gia_tri AS bigint) >  CAST(ky_vong AS bigint) THEN 'PASS'\n"
        "           WHEN kieu = 'le' AND CAST(gia_tri AS bigint) <= CAST(ky_vong AS bigint) THEN 'PASS'\n"
        "           ELSE 'FAIL'\n"
        "       END AS trang_thai\n"
        "FROM checks\n"
        "ORDER BY ma;\n"
        "GO\n\n"
    )

    parts.append(
        "-- -------------------------------------------------------------------\n"
        "-- 2. TONG HOP TRANG THAI\n"
        "-- -------------------------------------------------------------------\n"
        "SELECT nhom, trang_thai, COUNT(*) AS so_check\n"
        "FROM (\n"
        "    SELECT ma, nhom, gia_tri, ky_vong,\n"
        "           CASE\n"
        "               WHEN kieu = 'info' THEN 'INFO'\n"
        "               WHEN kieu = 'eq' AND CAST(gia_tri AS bigint) = CAST(ky_vong AS bigint) THEN 'PASS'\n"
        "               WHEN kieu = 'ge' AND CAST(gia_tri AS bigint) >= CAST(ky_vong AS bigint) THEN 'PASS'\n"
        "               WHEN kieu = 'gt' AND CAST(gia_tri AS bigint) >  CAST(ky_vong AS bigint) THEN 'PASS'\n"
        "               WHEN kieu = 'le' AND CAST(gia_tri AS bigint) <= CAST(ky_vong AS bigint) THEN 'PASS'\n"
        "               ELSE 'FAIL'\n"
        "           END AS trang_thai\n"
        "    FROM checks\n"
        ") t\n"
        "GROUP BY nhom, trang_thai\n"
        "ORDER BY nhom, trang_thai;\n"
        "GO\n\n"
    )

    parts.append(
        "PRINT '';\n"
        "PRINT 'HOAN TAT KIEM TRA. Doc bang \"trang_thai\" de biet PASS / FAIL.';\n"
        "GO\n"
    )

    out = "".join(parts)

    if args.check:
        if not OUT.exists():
            print(f"[FAIL] {OUT} khong ton tai")
            return 1
        cur = OUT.read_bytes()
        if cur.startswith(b"\xef\xbb\xbf"):
            cur = cur[3:]
        if cur.decode("utf-8") != out:
            print("[FAIL] 02_verify.sql KHONG khop registry. "
                  "Chay: python scripts/build_verify_sql.py")
            return 1
        print("[PASS] 02_verify.sql khop registry.")
        return 0

    OUT.write_bytes(out.encode("utf-8"))  # no BOM
    print(f"[OK] Da sinh {OUT} ({OUT.stat().st_size:,} bytes, UTF-8 no BOM) "
          f"voi {len(ALL_CHECKS)} check.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
