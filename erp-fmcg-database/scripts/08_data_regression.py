#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
08_data_regression.py — TEST HỒI QUY DỮ LIỆU (6C.2).
=====================================================
★ PHASE 6C.2 — "Test hồi quy dữ liệu"

Mục đích:
  Khi sửa schema / script import / code backend, phải chứng minh rằng
  dữ liệu NGHIỆP VỤ không bị đổi (49.844 đơn + 176.332 dòng...).
  Cơ chế: băm (hash) các tập dữ liệu chuẩn hoá, lưu snapshot vào
  `scripts/baseline/snapshot.json`, rồi so sánh với DB hiện tại.

Cách tính hash (ổn định, độc lập thứ tự dòng):
  - Mỗi bảng: `SELECT <cột cố định> FROM ... ORDER BY <khoá>`
  - Nối thành chuỗi với dấu `|`, hash SHA-256 theo TỪNG LÔ 5.000 dòng
    rồi hash lại danh sách hash lô → hash cuối ổn định theo thứ tự.

Bộ dữ liệu băm (mỗi mục là 1 "tập kiểm tra"):
  orders    : sales_orders (id, order_number, status, total_amount, ...)
  items     : sales_order_items (id, order_id, sku, qty, line_total)
  partners  : partners (id, code, name, current_debt, old_debt)
  products  : products (id, sku, name, base_price)
  stock     : stock_ledger (id, type, product, qty_in, qty_out, cost)
  pods      : delivery_pod (id, client_pod_id, status, cash fields)

Usage:
    # Tạo snapshot baseline (chạy 1 lần khi data đã đúng)
    python scripts/08_data_regression.py --snapshot

    # So sánh với baseline (chạy trong CI)
    python scripts/08_data_regression.py --check

    # Xem chi tiết khác biệt
    python scripts/08_data_regression.py --check --verbose

Exit code:
    0 = khớp baseline (hoặc chưa có baseline mà --snapshot)
    1 = KHÔNG khớp baseline -> CI fail
    3 = chưa có baseline -> chạy --snapshot trước
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from pathlib import Path
from typing import Any

try:
    import pyodbc
except ImportError:  # pragma: no cover
    print("LOI: chua cai pyodbc. Chay: python -m pip install -r requirements.txt",
          file=sys.stderr)
    raise SystemExit(2)

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent
BASELINE_DIR = HERE / "baseline"
SNAPSHOT_PATH = BASELINE_DIR / "snapshot.json"

CHUNK = 5_000


# ---------------------------------------------------------------- datasets
def datasets() -> dict[str, str]:
    return {
        "orders": """
            SELECT TOP (1000000)
                   o.id, o.order_number, o.order_date, o.status,
                   o.payment_status, o.subtotal, o.discount_amount,
                   o.tax_amount, o.total_amount, o.paid_amount,
                   o.old_debt_amount, o.failure_count
            FROM tenant.sales_orders o
            ORDER BY o.order_number, o.id
        """,
        "items": """
            SELECT TOP (2000000)
                   i.id, i.sales_order_id, i.line_number, i.product_sku,
                   i.uom_code, i.ordered_qty, i.base_qty, i.unit_price,
                   i.discount_percent, i.discount_amount,
                   i.line_subtotal, i.line_total, i.notes
            FROM tenant.sales_order_items i
            ORDER BY i.sales_order_id, i.line_number, i.id
        """,
        "partners": """
            SELECT TOP (100000)
                   p.id, p.partner_code, p.partner_name, p.partner_type,
                   p.route_code, p.phone, p.is_active,
                   p.current_debt, p.old_debt, p.old_debt_date,
                   p.geocoding_confidence, p.latitude, p.longitude
            FROM tenant.partners p
            ORDER BY p.partner_code, p.id
        """,
        "products": """
            SELECT TOP (100000)
                   pr.id, pr.sku, pr.product_name, pr.base_uom,
                   pr.base_price, pr.vat_rate, pr.is_active,
                   pr.barcode, pr.min_stock, pr.max_stock, pr.reorder_point
            FROM tenant.products pr
            ORDER BY pr.sku, pr.id
        """,
        "stock": """
            SELECT TOP (1000000)
                   l.id, l.transaction_date, l.transaction_type,
                   l.warehouse_id, l.product_id, l.uom_code,
                   l.quantity_in, l.quantity_out, l.unit_cost,
                   l.reference_type, l.reference_id, l.partner_id, l.notes
            FROM tenant.stock_ledger l
            ORDER BY l.transaction_date, l.id
        """,
        "pods": """
            SELECT TOP (1000000)
                   d.id, d.client_pod_id, d.trip_order_id, d.sales_order_id,
                   d.partner_id, d.delivery_status, d.gps_gate_result,
                   d.time_guard_result, d.distance_to_partner_m,
                   d.new_order_cash, d.new_order_transfer, d.new_order_credit,
                   d.old_debt_cash, d.old_debt_transfer, d.old_debt_credit,
                   d.total_collected, d.created_at
            FROM tenant.delivery_pod d
            ORDER BY d.created_at, d.id
        """,
    }


def hash_dataset(cur: pyodbc.Cursor, sql: str) -> dict[str, Any]:
    """Băm 1 tập dữ liệu. Trả về {hash, row_count, chunk_hashes}."""
    cur.execute(sql)
    chunk_hashes: list[str] = []
    row_count = 0
    buf: list[str] = []

    while True:
        rows = cur.fetchmany(CHUNK)
        if not rows:
            break
        for r in rows:
            buf.append("|".join("" if v is None else str(v) for v in r))
        row_count += len(rows)
        chunk_hashes.append(
            hashlib.sha256("\n".join(buf).encode("utf-8")).hexdigest())
        buf = []

    final = hashlib.sha256(
        "|".join(chunk_hashes).encode("utf-8")).hexdigest()
    return {"hash": final, "row_count": row_count, "chunks": chunk_hashes}


def compute_all(conn: pyodbc.Connection) -> dict[str, Any]:
    cur = conn.cursor()
    out: dict[str, Any] = {}
    for name, sql in datasets().items():
        print(f"  [hash] {name} ...", end=" ", flush=True)
        res = hash_dataset(cur, sql)
        out[name] = res
        print(f"{res['hash'][:16]}... ({res['row_count']:,} dong)")
    try:
        cur.close()
    except Exception:
        pass
    return out


def main() -> int:
    ap = argparse.ArgumentParser(
        description="ERP-FMCG - test hoi quy du lieu (Phase 6C.2)")
    ap.add_argument("--server", default=os.environ.get("ERP_FMCG_SERVER", r".\SQLEXPRESS"))
    ap.add_argument("--database", default=os.environ.get("ERP_FMCG_DB", "ERP_FMCG"))
    ap.add_argument("--snapshot", action="store_true",
                    help="Ghi snapshot baseline moi (ghi de file cu)")
    ap.add_argument("--check", action="store_true",
                    help="So sanh voi baseline (mac dinh neu khong co --snapshot)")
    ap.add_argument("--verbose", action="store_true",
                    help="In chi tiet dong du lieu khac biet")
    ap.add_argument("--output", default=None, help="Ghi them bao cao JSON")
    args = ap.parse_args()

    cs = (f"DRIVER={{SQL Server}};SERVER={args.server};"
          f"DATABASE={args.database};Trusted_Connection=yes;"
          "Encrypt=no;TrustServerCertificate=yes;")
    try:
        conn = pyodbc.connect(cs, timeout=60)
    except Exception as exc:
        print(f"LOI ket noi '{args.server}/{args.database}': {exc}", file=sys.stderr)
        return 2

    print("=" * 78)
    print(" ERP-FMCG - TEST HOI QUY DU LIEU (6C.2)")
    print("=" * 78)

    try:
        current = compute_all(conn)
    finally:
        conn.close()

    if args.snapshot:
        BASELINE_DIR.mkdir(parents=True, exist_ok=True)
        payload = {
            "generated_at": __import__("datetime").datetime.now().isoformat(
                timespec="seconds"),
            "server": args.server,
            "database": args.database,
            "datasets": current,
        }
        SNAPSHOT_PATH.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"\n  [OK] Da ghi baseline: {SNAPSHOT_PATH}")
        return 0

    # --- chế độ check ---
    if not SNAPSHOT_PATH.exists():
        print(f"\n  [WARN] Chua co baseline tai {SNAPSHOT_PATH}.")
        print("         Chay: python scripts/08_data_regression.py --snapshot")
        return 3

    baseline = json.loads(SNAPSHOT_PATH.read_text(encoding="utf-8"))
    base_ds = baseline.get("datasets", {})

    print("\n" + "-" * 78)
    print(f"{'Dataset':<14}{'Baseline':<20}{'Hien tai':<20}{'Dong':<16}Trang thai")
    print("-" * 78)

    all_ok = True
    diffs: list[str] = []
    for name, cur_res in current.items():
        base = base_ds.get(name)
        if base is None:
            print(f"{name:<14}{'-':<20}{cur_res['hash'][:16]:<20}"
                  f"{cur_res['row_count']:<16}THIEU BASELINE")
            all_ok = False
            diffs.append(f"{name}: khong co trong baseline")
            continue
        same = base["hash"] == cur_res["hash"]
        if not same:
            all_ok = False
            diffs.append(
                f"{name}: hash khac\n"
                f"    baseline = {base['hash']}\n"
                f"    hien tai = {cur_res['hash']}\n"
                f"    so dong   = {base['row_count']} -> {cur_res['row_count']}"
            )
        print(f"{name:<14}{base['hash'][:16]:<20}{cur_res['hash'][:16]:<20}"
              f"{base['row_count']:,}->{cur_res['row_count']:<9}"
              f"{'PASS' if same else 'FAIL'}")

    print("-" * 78)
    if all_ok:
        print("  KET QUA: PASS - du lieu khop baseline.")
    else:
        print("  KET QUA: FAIL - du lieu da thay doi so voi baseline:")
        for d in diffs:
            print(f"    - {d}")
        print("\n  Neu thay doi la CAN THIET (vd: migration moi), chay lai:")
        print("      python scripts/08_data_regression.py --snapshot")
    print("=" * 78)

    if args.output:
        out = Path(args.output)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps({
            "baseline_generated_at": baseline.get("generated_at"),
            "current": current,
            "match": all_ok,
            "diffs": diffs,
        }, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"  Da xuat: {out}")

    return 0 if all_ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
