#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
02_verify_data.py - Đối soát dữ liệu ERP_FMCG sau khi import.

Chạy toàn bộ kiểm tra A1-A21, in báo cáo ra console và xuất file
CSV/JSON (tuỳ chọn) để đưa vào tài liệu nghiệm thu.

Usage:
    python scripts/02_verify_data.py --server ".\\SQLEXPRESS" --database "ERP_FMCG"
    python scripts/02_verify_data.py --output "reports/"
    python scripts/02_verify_data.py --json     # in ra stdout duy nhất

Exit code:
    0 = tất cả kiểm tra PASS (hoặc INFO)
    1 = có ít nhất 1 kiểm tra CHECK
    2 = lỗi kết nối / lỗi SQL
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

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


# ---------------------------------------------------------------- checks
CHECKS: list[tuple[str, str, str]] = [
    # (mã, mô tả, SQL)
    ("A1", "categories = 9",
     "SELECT COUNT(*) FROM tenant.categories"),
    ("A2", "products hop le (141 SKU that tu RDT, khong rác)",
     """SELECT COUNT(*) FROM tenant.products
        WHERE sku <> 'RD-FACTORY-AGGREGATED'
          AND sku NOT IN ('10000001', '10000002', '10000004', '10000005',
                          '10000006', '10000007', '10000008', '10000009',
                          '10000010')"""),
    ("A3", "partners",
     "SELECT COUNT(*) FROM tenant.partners"),
    ("A4", "sales_orders",
     "SELECT COUNT(*) FROM tenant.sales_orders"),
    ("A5", "sales_order_items",
     "SELECT COUNT(*) FROM tenant.sales_order_items"),
    ("A6", "items NULL / AGGREGATED = 0",
     """SELECT COUNT(*) FROM tenant.sales_order_items
        WHERE product_id IS NULL OR product_sku = 'RD-FACTORY-AGGREGATED'"""),
    ("A7", "FK items -> orders = 0",
     """SELECT COUNT(*) FROM tenant.sales_order_items i
        LEFT JOIN tenant.sales_orders o ON o.id = i.sales_order_id
        WHERE o.id IS NULL"""),
    ("A8", "FK items -> products = 0",
     """SELECT COUNT(*) FROM tenant.sales_order_items i
        LEFT JOIN tenant.products p ON p.id = i.product_id
        WHERE p.id IS NULL"""),
    ("A9", "FK orders -> partners = 0",
     """SELECT COUNT(*) FROM tenant.sales_orders o
        LEFT JOIN tenant.partners p ON p.id = o.customer_id
        WHERE p.id IS NULL"""),
    ("A10", "FK orders -> warehouses = 0",
     """SELECT COUNT(*) FROM tenant.sales_orders o
        LEFT JOIN tenant.warehouses w ON w.id = o.warehouse_id
        WHERE w.id IS NULL"""),
    ("A11", "cong thuc tong don (sai so <= 1) = 0",
     """SELECT COUNT(*) FROM tenant.sales_orders
        WHERE ABS((subtotal - discount_amount + tax_amount) - total_amount) > 1"""),
    ("A12", "items qty > 1e6 (loai loi phinh 1000x) = 0",
     "SELECT COUNT(*) FROM tenant.sales_order_items WHERE ordered_qty > 1000000"),
    ("A13", "audit_logs ton tai",
     """SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'audit_logs'"""),
    ("A14", "import_jobs >= 1",
     "SELECT COUNT(*) FROM tenant.import_jobs"),
    ("A15", "price_lists > 0",
     "SELECT COUNT(*) FROM tenant.price_lists"),
    ("A16", "temporal tables >= 5",
     "SELECT COUNT(*) FROM sys.tables WHERE temporal_type = 2"),
    ("A17", "trung order_number = 0",
     """SELECT COUNT(*) FROM (SELECT order_number FROM tenant.sales_orders
        GROUP BY order_number HAVING COUNT(*) > 1) d"""),
    ("A18", "trung sku = 0",
     """SELECT COUNT(*) FROM (SELECT sku FROM tenant.products
        GROUP BY sku HAVING COUNT(*) > 1) d"""),
    ("A19", "trung partner_code = 0",
     """SELECT COUNT(*) FROM (SELECT partner_code FROM tenant.partners
        GROUP BY partner_code HAVING COUNT(*) > 1) d"""),
]

# (mã, kỳ vọng dạng chuỗi, kiểu so sánh)
EXPECTED: dict[str, tuple[str, str]] = {
    "A1": ("9", "eq"),
    "A2": ("141", "eq"),
    "A3": ("2723", "eq"),          # 2.723 (41 KH khong giao dich RDT van duoc giu)
    "A4": ("49844", "eq"),         # 49.833 RDT + 11 seed
    "A5": ("176000", "ge"),
    "A6": ("0", "eq"),
    "A7": ("0", "eq"),
    "A8": ("0", "eq"),
    "A9": ("0", "eq"),
    "A10": ("0", "eq"),
    "A11": ("0", "eq"),
    "A12": ("0", "eq"),
    "A13": ("1", "eq"),
    "A14": ("1", "ge"),
    "A15": ("1", "gt"),
    "A16": ("5", "ge"),
    "A17": ("0", "eq"),
    "A18": ("0", "eq"),
    "A19": ("0", "eq"),
}

EXTRA_QUERIES: dict[str, str] = {
    "A20 don CANCELLED": "SELECT COUNT(*) FROM tenant.sales_orders WHERE status = 'CANCELLED'",
    "A21 import_errors da ghi": "SELECT COUNT(*) FROM tenant.import_errors",
    "A22 uom_conversions": "SELECT COUNT(*) FROM tenant.uom_conversions",
    "A23 user_accounts": "SELECT COUNT(*) FROM tenant.user_accounts",
    "A24 tong tien don hang": "SELECT CAST(SUM(total_amount) AS DECIMAL(38,2)) FROM tenant.sales_orders",
    "A25 doi soat line_total vs total_amount (so don lech)":
     "SELECT COUNT(*) FROM ("
     "  SELECT o.id FROM tenant.sales_orders o"
     "  JOIN tenant.sales_order_items i ON i.sales_order_id = o.id"
     "  GROUP BY o.id, o.total_amount, o.tax_amount"
     "  HAVING ABS(SUM(i.line_total) - (o.total_amount - o.tax_amount)) > 1"
     ") d",
    "A26 import_errors theo ma (nhom)":
     "SELECT COUNT(*) FROM tenant.import_errors",
}


def connect(server: str, database: str) -> pyodbc.Connection:
    cs = (
        f"DRIVER={{SQL Server}};SERVER={server};"
        f"DATABASE={database};Trusted_Connection=yes;"
        "Encrypt=no;TrustServerCertificate=yes;"
    )
    return pyodbc.connect(cs, timeout=15)


def evaluate(code: str, value: int) -> str:
    """So sanh gia tri voi ky vong -> PASS / CHECK / INFO."""
    if code not in EXPECTED:
        return "INFO"
    exp, mode = EXPECTED[code]
    e = int(exp)
    if mode == "eq":
        return "PASS" if value == e else "CHECK"
    if mode == "ge":
        return "PASS" if value >= e else "CHECK"
    if mode == "gt":
        return "PASS" if value > e else "CHECK"
    return "INFO"


def main() -> int:
    ap = argparse.ArgumentParser(description="ERP-FMCG - doi soat du lieu sau import")
    ap.add_argument("--server", default=r".\SQLEXPRESS")
    ap.add_argument("--database", default="ERP_FMCG")
    ap.add_argument("--output", default=None,
                    help="Thu muc xuat CSV/JSON (vd: reports/)")
    ap.add_argument("--json", action="store_true",
                    help="In ket qua ra stdout duy nhất dang JSON")
    args = ap.parse_args()

    try:
        conn = connect(args.server, args.database)
    except Exception as exc:
        print(f"LOI ket noi '{args.server}/{args.database}': {exc}", file=sys.stderr)
        return 2

    cur = conn.cursor()
    results: list[dict[str, Any]] = []

    try:
        for code, desc, sql in CHECKS:
            cur.execute(sql)
            row = cur.fetchone()
            val = int(row[0]) if row and row[0] is not None else 0
            status = evaluate(code, val)
            exp = EXPECTED.get(code, ("", "eq"))[0]
            results.append({
                "ma": code, "mo_ta": desc, "gia_tri": val,
                "ky_vong": exp, "trang_thai": status,
            })

        for desc, sql in EXTRA_QUERIES.items():
            cur.execute(sql)
            row = cur.fetchone()
            val = row[0] if row else None
            results.append({
                "ma": desc.split()[0], "mo_ta": desc,
                "gia_tri": val if isinstance(val, int) else str(val),
                "ky_vong": "-", "trang_thai": "INFO",
            })
    except Exception as exc:
        print(f"LOI truy van: {exc}", file=sys.stderr)
        conn.close()
        return 2
    finally:
        try:
            cur.close()
            conn.close()
        except Exception:
            pass

    n_pass = sum(1 for r in results if r["trang_thai"] == "PASS")
    n_check = sum(1 for r in results if r["trang_thai"] == "CHECK")
    n_info = sum(1 for r in results if r["trang_thai"] == "INFO")

    if args.json:
        print(json.dumps({
            "server": args.server, "database": args.database,
            "pass": n_pass, "check": n_check, "info": n_info,
            "results": results,
        }, ensure_ascii=False, indent=2))
    else:
        print("=" * 78)
        print(f" ERP-FMCG - DOI SOAT DU LIEU  ({args.server} / {args.database})")
        print("=" * 78)
        print(f"{'Ma':<6}{'Mo ta':<52}{'Gia tri':>14}  {'Ky vong':<10} {'Trang thai':<8}")
        print("-" * 78)
        for r in results:
            val = r["gia_tri"]
            val_s = f"{val:,}" if isinstance(val, int) else str(val)
            print(f"{r['ma']:<6}{r['mo_ta'][:51]:<52}{val_s:>14}  "
                  f"{r['ky_vong']:<10} {r['trang_thai']:<8}")
        print("-" * 78)
        print(f"  PASS: {n_pass}   CHECK: {n_check}   INFO: {n_info}")
        print("=" * 78)

    if args.output:
        out_dir = Path(args.output)
        out_dir.mkdir(parents=True, exist_ok=True)
        csv_path = out_dir / "verify_report.csv"
        with csv_path.open("w", newline="", encoding="utf-8-sig") as fh:
            w = csv.DictWriter(fh, fieldnames=["ma", "mo_ta", "gia_tri",
                                               "ky_vong", "trang_thai"])
            w.writeheader()
            w.writerows(results)
        json_path = out_dir / "verify_report.json"
        json_path.write_text(json.dumps(results, ensure_ascii=False, indent=2),
                             encoding="utf-8")
        if not args.json:
            print(f"\n  Da xuat: {csv_path}")
            print(f"  Da xuat: {json_path}")

    return 1 if n_check else 0


if __name__ == "__main__":
    raise SystemExit(main())
