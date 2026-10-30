#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
PHASE 6A.3 — NẠP TỒN KHO ĐẦU KỲ (OPENING BALANCE)
=====================================================
Hiện trạng trước Phase 6A.3:
  - tenant.stock_ledger chỉ có 16 dòng `SALE` (sinh ra từ test POD)
  - tenant.inventory_balances có 4 dòng với quantity_on_hand = -4  (ÂM)
  => Mọi logic trừ kho sẽ cho âm, báo cáo tồn kho sai, KPI sai.

Nguyên tắc của script này (idempotent, chạy lại nhiều lần vẫn an toàn):
  1. Tính `net = SUM(quantity_in - quantity_out)` theo (warehouse, product, uom).
  2. Với mỗi cặp (kho, SKU) có `net < 0`:
       - ghi 1 bút toán `OPENING` với `quantity_in = -net`
         => cân bằng số dư về 0, ledger và inventory_balances khớp.
  3. `unit_cost` lấy theo giá bán gần nhất trong `tenant.price_lists`
     (fallback `products.base_price`).
  4. Trigger `trg_stockledger_after_insert` tự MERGE sang
     `tenant.inventory_balances` — không tự update balance để tránh lệch.

Chạy:  python scripts/06_seed_opening_stock.py
"""
import os
import sys
import pyodbc

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

SERVER = os.environ.get("ERP_FMCG_SERVER", r".\SQLEXPRESS")
DB = os.environ.get("ERP_FMCG_DB", "ERP_FMCG")
DRY_RUN = os.environ.get("ERP_FMCG_DRY_RUN", "0") == "1"

CS = (
    f"DRIVER={{SQL Server}};SERVER={SERVER};DATABASE={DB};"
    "Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
)

CHECKS = []


def check(name, ok, detail=""):
    CHECKS.append((name, ok, detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" — {detail}" if detail else ""))


def main():
    conn = pyodbc.connect(CS, timeout=60, autocommit=False)
    cur = conn.cursor()

    print("=" * 70)
    print("PHASE 6A.3 — NAP TON KHO DAU KY (OPENING)")
    print(f"Server={SERVER}  DB={DB}  dry_run={DRY_RUN}")
    print("=" * 70)

    # ---------------------------------------------------------------
    # 0. Pre-flight: trigger phải tồn tại và đang bật
    # ---------------------------------------------------------------
    cur.execute(
        "SELECT COUNT(*) FROM sys.triggers "
        "WHERE parent_id = OBJECT_ID('tenant.stock_ledger') AND is_disabled = 0"
    )
    trig = cur.fetchone()[0]
    check("Trigger trg_stockledger_after_insert dang bat", trig > 0, f"so trigger = {trig}")
    if trig == 0:
        print("  -> Trigger khong bat, khong the nap ton. Dung.")
        conn.rollback()
        return 1

    # ---------------------------------------------------------------
    # 1. Tính net theo (warehouse, product, uom)
    # ---------------------------------------------------------------
    cur.execute(
        """
        SELECT l.warehouse_id, l.product_id, l.uom_code,
               SUM(l.quantity_in - l.quantity_out) AS net_qty,
               p.sku
        FROM tenant.stock_ledger l
        JOIN tenant.products p ON p.id = l.product_id
        GROUP BY l.warehouse_id, l.product_id, l.uom_code, p.sku
        HAVING SUM(l.quantity_in - l.quantity_out) < 0
        ORDER BY p.sku
        """
    )
    negatives = cur.fetchall()
    print(f"\n  Canh bao ton am: {len(negatives)} cap (kho, SKU)")
    for w, p, uom, net, sku in negatives:
        print(f"    - {sku:12} uom={uom:5} net={net}")

    inserted = 0
    for w, pid, uom, net, sku in negatives:
        # Giá vốn tham chiếu: giá trong price_lists (mới nhất, active),
        # fallback `products.base_price`.
        cur.execute(
            """
            SELECT TOP 1 unit_price
            FROM tenant.price_lists pl
            WHERE pl.product_id = ? AND pl.is_active = 1
            ORDER BY pl.effective_date DESC, pl.priority ASC
            """,
            (pid,),
        )
        row = cur.fetchone()
        cost = row[0] if row else None
        if cost is None:
            cur.execute("SELECT base_price FROM tenant.products WHERE id = ?", (pid,))
            row = cur.fetchone()
            cost = row[0] if row else 0

        qty = -net
        notes = (
            f"PHASE 6A.3 opening balance (can bang ton am truoc Phase 6A.4) - SKU {sku}"
        )
        if DRY_RUN:
            print(f"  [DRY] INSERT OPENING {sku} +{qty} uom={uom} cost={cost}")
            inserted += 1
            continue

        cur.execute(
            """
            INSERT INTO tenant.stock_ledger (
                transaction_date, transaction_time, transaction_type,
                warehouse_id, product_id, uom_code,
                quantity_in, quantity_out, unit_cost,
                reference_type, reference_id, partner_id, notes,
                created_at, created_by
            )
            VALUES (CAST(GETDATE() AS DATE), GETDATE(), 'OPENING',
                    ?, ?, ?, ?, 0, ?, 'OPENING', NULL, NULL, ?, GETDATE(), NULL)
            """,
            (w, pid, uom, qty, cost, notes),
        )
        inserted += 1

    if not DRY_RUN:
        conn.commit()
        print(f"\n  Da ghi {inserted} but toan OPENING.")
    else:
        conn.rollback()
        print(f"\n  [DRY] se ghi {inserted} but toan OPENING.")

    # ---------------------------------------------------------------
    # 4. Verify
    # ---------------------------------------------------------------
    cur.execute(
        """
        SELECT COUNT(*) FROM (
            SELECT warehouse_id, product_id, uom_code
            FROM tenant.stock_ledger
            GROUP BY warehouse_id, product_id, uom_code
            HAVING SUM(quantity_in - quantity_out) < 0
        ) x
        """
    )
    still_neg = cur.fetchone()[0]
    check("Khong con ton am trong stock_ledger", still_neg == 0, f"con {still_neg} cap am")

    cur.execute(
        """
        SELECT COUNT(*) FROM tenant.inventory_balances WHERE quantity_on_hand < 0
        """
    )
    neg_bal = cur.fetchone()[0]
    check("Khong con ton am trong inventory_balances", neg_bal == 0, f"con {neg_bal} dong am")

    # ledger <-> balances khop?
    cur.execute(
        """
        WITH led AS (
            SELECT warehouse_id, product_id, uom_code,
                   SUM(quantity_in - quantity_out) AS net
            FROM tenant.stock_ledger
            GROUP BY warehouse_id, product_id, uom_code
        )
        SELECT COUNT(*) FROM led l
        FULL JOIN tenant.inventory_balances b
          ON  b.warehouse_id = l.warehouse_id
          AND b.product_id   = l.product_id
          AND b.uom_code     = l.uom_code
        WHERE ISNULL(l.net, 0) <> ISNULL(b.quantity_on_hand, 0)
        """
    )
    mismatch = cur.fetchone()[0]
    check("stock_ledger == inventory_balances", mismatch == 0, f"{mismatch} cap lech")

    cur.execute("SELECT COUNT(*) FROM tenant.stock_ledger WHERE transaction_type = 'OPENING'")
    opening_rows = cur.fetchone()[0]
    check("Co but toan OPENING trong ledger", opening_rows > 0, f"{opening_rows} dong OPENING")

    conn.close()

    ok = all(c[1] for c in CHECKS)
    print("\n" + "=" * 70)
    print(f"KET QUA: {sum(1 for c in CHECKS if c[1])}/{len(CHECKS)} check PASS")
    print("=" * 70)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
