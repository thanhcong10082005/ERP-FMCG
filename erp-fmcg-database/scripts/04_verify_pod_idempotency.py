#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
================================================================================
 ERP-FMCG - KIỂM TRA IDEMPOTENCY CỦA POD
================================================================================
 Chứng minh rằng `client_pod_id` thực sự chống được double-POD.

 Bối cảnh
 ---------
   Lỗi double-POD đã xảy ra THẬT trên production Supabase:
   `Supabase_export/delivery_pod_rows.csv` có 13 dòng POD, trong đó
   6 nhóm trùng (trip_order_id, so_id, partner_id, total_collected) —
   tạo cách nhau từ 7 giây đến 1 ngày.

   Nguyên nhân gốc: Driver App `driverStore.ts` gọi `savePendingPOD()`
   KHÔNG ĐIỀU KIỆN sau khi POST online thành công → cùng 1 lần giao
   bị ghi 2 lần (1 lần online + 1 lần trong queue).

 Cách kiểm tra
 -------------
   1. Insert 1 POD với `client_pod_id` cố định.
   2. Insert LẠI cùng `client_pod_id` → phải bị constraint `UQ_delivery_pod_client`
      từ chối.
   3. `COUNT(*)` vẫn = 1 → server chỉ tạo 1 POD duy nhất.
   4. Xóa dòng test (dọn dẹp).

 Usage
 -----
   python 04_verify_pod_idempotency.py
   python 04_verify_pod_idempotency.py --keep    # giữ dòng test để kiểm tra tay

================================================================================
"""

from __future__ import annotations

import argparse
import os
import sys
import uuid
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

try:
    import pyodbc
except ImportError:  # pragma: no cover
    print("Thieu pyodbc. Chay: pip install pyodbc", file=sys.stderr)
    raise

DEFAULT_SERVER = os.environ.get("ERP_FMCG_SERVER", r".\SQLEXPRESS")
DEFAULT_DATABASE = os.environ.get("ERP_FMCG_DATABASE", "ERP_FMCG")

# client_pod_id cố định để test có thể lặp lại
TEST_CLIENT_POD_ID = "11111111-1111-1111-1111-111111111111"


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify client_pod_id idempotency"
    )
    parser.add_argument("--server", default=DEFAULT_SERVER)
    parser.add_argument("--database", default=DEFAULT_DATABASE)
    parser.add_argument("--keep", action="store_true",
                        help="Giu dong test trong DB (mac dinh: xoa sau khi verify)")
    args = parser.parse_args()

    cs = (
        f"DRIVER={{SQL Server}};SERVER={args.server};"
        f"DATABASE={args.database};Trusted_Connection=yes;"
    )
    conn = pyodbc.connect(cs, timeout=30, autocommit=False)
    conn.timeout = 120
    cur = conn.cursor()

    ok = True
    try:
        # --- Chuan bi (du lieu toi thieu) ---
        # delivery_trip_orders khong co partner_id -> lay qua sales_orders
        cur.execute(
            """
            SELECT TOP 1
                   d.id, d.sales_order_id, o.customer_id, t.warehouse_id, t.id
            FROM tenant.delivery_trip_orders d
            JOIN tenant.sales_orders o ON o.id = d.sales_order_id
            JOIN tenant.delivery_trips t ON t.id = d.trip_id
            """
        )
        row = cur.fetchone()
        if row is None:
            print("[WARN] Chua co du lieu delivery_trip_orders trong DB.")
            print("       Hay tao chuyen giao hang truoc, hoac bo qua buoc nay.")
            return 0

        trip_order_id = str(row[0])
        sales_order_id = str(row[1])
        partner_id = str(row[2])
        warehouse_id = str(row[3])
        trip_id = str(row[4])

        # --- Xoa dong test cu (neu con) ---
        cur.execute(
            "DELETE FROM tenant.delivery_pod WHERE client_pod_id = ?",
            TEST_CLIENT_POD_ID,
        )
        conn.commit()

        insert_sql = """
            INSERT INTO tenant.delivery_pod
                (client_pod_id, trip_order_id, sales_order_id, partner_id,
                 delivery_status, total_collected, created_by)
            VALUES (?, ?, ?, ?, 'DELIVERED', 0, NULL)
        """

        # --- Lan 1: insert that ---
        print("=" * 70)
        print(" KIEM TRA IDEMPOTENCY client_pod_id")
        print("=" * 70)
        cur.execute(insert_sql, TEST_CLIENT_POD_ID, trip_order_id,
                    sales_order_id, partner_id)
        conn.commit()
        print(" [1] Lan 1: INSERT thanh cong (client_pod_id = %s)" % TEST_CLIENT_POD_ID)

        # --- Lan 2: insert lai cung client_pod_id ---
        rejected = False
        err_msg = ""
        try:
            cur.execute(insert_sql, TEST_CLIENT_POD_ID, trip_order_id,
                        sales_order_id, partner_id)
            conn.commit()
        except Exception as exc:
            conn.rollback()
            rejected = True
            err_msg = str(exc)
            print(" [2] Lan 2: BI TU CHOI (nhu mong doi)")
            print("     -> %s" % err_msg.splitlines()[0][:150])

        if not rejected:
            print(" [2] Lan 2: KHONG bi tu choi  <-- FAIL!")
            ok = False

        # --- Dem so dong ---
        cur.execute(
            "SELECT COUNT(*) FROM tenant.delivery_pod WHERE client_pod_id = ?",
            TEST_CLIENT_POD_ID,
        )
        cnt = cur.fetchone()[0]
        print(" [3] COUNT(*) cho client_pod_id nay = %d (ky vong = 1)" % cnt)
        if cnt != 1:
            print("     -> FAIL: van con > 1 dong!")
            ok = False

        # --- Kiem tra constraint ton tai ---
        cur.execute(
            """
            SELECT COUNT(*) FROM sys.key_constraints
            WHERE name = 'UQ_delivery_pod_client' AND type = 'UQ'
            """
        )
        has_uq = cur.fetchone()[0]
        print(" [4] UQ_delivery_pod_client ton tai = %s" % ("YES" if has_uq else "NO"))
        if not has_uq:
            ok = False

        # --- Dọn dẹp ---
        if not args.keep:
            cur.execute(
                "DELETE FROM tenant.delivery_pod WHERE client_pod_id = ?",
                TEST_CLIENT_POD_ID,
            )
            conn.commit()
            print(" [5] Da xoa dong test")
        else:
            print(" [5] Giu dong test (--keep)")

        print("=" * 70)
        print(" KET QUA: %s" % ("PASS" if ok else "FAIL"))
        print("=" * 70)
        return 0 if ok else 1

    finally:
        try:
            conn.close()
        except Exception:
            pass


if __name__ == "__main__":
    raise SystemExit(main())
