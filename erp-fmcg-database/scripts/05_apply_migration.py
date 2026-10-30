#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
================================================================================
 ERP-FMCG - AP DUNG MIGRATION DISPATCHER/EOD (09_dispatcher_eod.sql)
================================================================================
 Script nay:
   1. Doc file `database/02_migration/09_dispatcher_eod.sql`
   2. Tach thanh cac batch (theo GO) va chay tuan tu tren SQL Server
   3. In ket qua verify: bang EOD da tao, CHECK constraint da co, trigger con song

 Vay sao can script va khong dung sqlcmd?
   - `GO` khong phai lenh SQL Server ma la phan tach batch cua sqlcmd/SSMS.
     Khi chay qua pyodbc, phai tach thu cong.
   - Script dung chay duoc tu Python ma khong can SQL Server Management Studio.

 Usage
 -----
   python 05_apply_migration.py
   python 05_apply_migration.py --server ".\\SQLEXPRESS"

 Ket qua mong doi
 ---------------
   [PASS] tenant.eod_reconciliations          : 0 dong (bang moi)
   [PASS] tenant.eod_reconciliation_items     : 0 dong (bang moi)
   [PASS] CK_eod_cash_action                  : ton tai
   [PASS] CK_eod_items_action                 : ton tai
   [PASS] CK_eod_status                       : ton tai
   [PASS] CK_eod_item_status                  : ton tai
   [PASS] CK_delivery_pod_gps_gate_result     : ton tai
   [PASS] CK_delivery_pod_time_guard_result   : ton tai
   [PASS] sp_purge_sales_orders_history       : ton tai
================================================================================
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

try:
    import pyodbc
except ImportError:  # pragma: no cover
    print("[FATAL] Thieu pyodbc. Cai: pip install pyodbc", file=sys.stderr)
    sys.exit(2)

REPO = Path(__file__).resolve().parents[1]
MIGRATION_FILE = REPO / "database" / "02_migration" / "09_dispatcher_eod.sql"

DEFAULT_SERVER = os.environ.get("ERP_FMCG_SERVER", r".\SQLEXPRESS")
DEFAULT_DB = os.environ.get("ERP_FMCG_DB", "ERP_FMCG")

GREEN, RED, YELLOW, CYAN, RESET = "\033[32m", "\033[31m", "\033[33m", "\033[36m", "\033[0m"


def connect(server: str, database: str):
    cs = (
        f"DRIVER={{SQL Server}};SERVER={server};"
        f"DATABASE={database};Trusted_Connection=True;"
        "TrustServerCertificate=True;Encrypt=False"
    )
    return pyodbc.connect(cs, timeout=30, autocommit=True)


def split_batches(sql: str) -> list[str]:
    """Tach SQL thanh cac batch theo 'GO' (khong phan biet chuoi)."""
    batches, current = [], []
    for line in sql.splitlines():
        if line.strip().upper() == "GO":
            batch = "\n".join(current).strip()
            if batch:
                batches.append(batch)
            current = []
        else:
            current.append(line)
    tail = "\n".join(current).strip()
    if tail:
        batches.append(tail)
    return batches


def run_migration(conn) -> int:
    if not MIGRATION_FILE.exists():
        print(f"[FATAL] Khong tim thay file migration: {MIGRATION_FILE}", file=sys.stderr)
        return 2

    sql = MIGRATION_FILE.read_text(encoding="utf-8")
    batches = split_batches(sql)
    print(f"{CYAN}[INFO]{RESET} Doc {MIGRATION_FILE.name}: {len(batches)} batch")

    cursor = conn.cursor()
    for i, batch in enumerate(batches, 1):
        try:
            cursor.execute(batch)
            for row in cursor.fetchall() if cursor.description else []:
                msg = " | ".join(str(v) for v in row if v is not None)
                if msg.strip():
                    print(f"       {msg}")
            print(f"  {GREEN}[OK]{RESET} batch {i}/{len(batches)}")
        except Exception as exc:  # noqa: BLE001
            print(f"  {RED}[FAIL]{RESET} batch {i}/{len(batches)}: {exc}", file=sys.stderr)
            print(f"       SQL:\n{batch[:600]}", file=sys.stderr)
            return 1
    return 0


VERIFY = [
    ("bang tenant.eod_reconciliations",
     "SELECT COUNT(*) FROM sys.tables WHERE name = 'eod_reconciliations' AND schema_id = SCHEMA_ID('tenant')"),
    ("bang tenant.eod_reconciliation_items",
     "SELECT COUNT(*) FROM sys.tables WHERE name = 'eod_reconciliation_items' AND schema_id = SCHEMA_ID('tenant')"),
    ("CHECK CK_eod_cash_action",
     "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_eod_cash_action'"),
    ("CHECK CK_eod_items_action",
     "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_eod_items_action'"),
    ("CHECK CK_eod_status",
     "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_eod_status'"),
    ("CHECK CK_eod_item_status",
     "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_eod_item_status'"),
    ("CHECK CK_delivery_pod_gps_gate_result",
     "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_delivery_pod_gps_gate_result'"),
    ("CHECK CK_delivery_pod_time_guard_result",
     "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_delivery_pod_time_guard_result'"),
    ("ham sp_purge_sales_orders_history",
     "SELECT COUNT(*) FROM sys.objects WHERE name = 'sp_purge_sales_orders_history'"),
]


def verify(conn) -> tuple[int, int]:
    cursor = conn.cursor()
    passed = failed = 0
    print()
    print("=" * 70)
    print("KET QUA VERIFY MIGRATION DISPATCHER/EOD")
    print("=" * 70)

    for label, sql in VERIFY:
        try:
            cursor.execute(sql)
            count = cursor.fetchone()[0]
            if count and count > 0:
                print(f"  {GREEN}[PASS]{RESET} {label}: {count}")
                passed += 1
            else:
                print(f"  {RED}[FAIL]{RESET} {label}: khong tim thay")
                failed += 1
        except Exception as exc:  # noqa: BLE001
            print(f"  {RED}[FAIL]{RESET} {label}: {exc}", file=sys.stderr)
            failed += 1

    # So dong EOD (bang moi => 0)
    for table in ("eod_reconciliations", "eod_reconciliation_items"):
        try:
            cursor.execute(f"SELECT COUNT(*) FROM tenant.{table}")
            n = cursor.fetchone()[0]
            print(f"  {GREEN}[INFO]{RESET} tenant.{table}: {n} dong")
        except Exception as exc:  # noqa: BLE001
            print(f"  {YELLOW}[WARN]{RESET} khong doc duoc tenant.{table}: {exc}")

    print("=" * 70)
    print(f"Tong: {GREEN}{passed} PASS{RESET} / {RED}{failed} FAIL{RESET}")
    print("=" * 70)
    return passed, failed


def main() -> int:
    parser = argparse.ArgumentParser(description="Ap dung migration dispatcher/EOD")
    parser.add_argument("--server", default=DEFAULT_SERVER)
    parser.add_argument("--database", default=DEFAULT_DB)
    args = parser.parse_args()

    print(f"{CYAN}[INFO]{RESET} Server={args.server} Database={args.database}")
    try:
        conn = connect(args.server, args.database)
    except Exception as exc:  # noqa: BLE001
        print(f"{RED}[FATAL]{RESET} Khong ket noi duoc DB: {exc}", file=sys.stderr)
        return 2

    rc = run_migration(conn)
    if rc != 0:
        print(f"{RED}[FATAL]{RESET} Migration that bai.", file=sys.stderr)
        conn.close()
        return rc

    passed, failed = verify(conn)
    conn.close()

    print()
    if failed == 0:
        print(f"{GREEN}[OK] MIGRATION THANH CONG{RESET}")
        return 0
    print(f"{RED}[FAIL] MIGRATION CO {failed} CHECK FAIL{RESET}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
