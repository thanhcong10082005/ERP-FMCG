#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
verify_registry.py — NGUỒN SỰ THẬT DUY NHẤT cho bộ kiểm tra dữ liệu.
=======================================================================
"Hợp nhất bộ verify"

Vấn đề trước khi hợp nhất bộ verify:
  - `02_verify.sql`            : 26 check viết thẳng trong SQL (không parse được)
  - `scripts/02_verify_data.py`: 22 check viết thẳng trong Python
  -> 2 nguồn kiểm tra trùng nhau, LỆCH NHAU theo thời gian.
     Thêm check mới phải sửa 2 chỗ -> dễ quên -> check không bao giờ chạy.

Cách sửa:
  1. TẠO FILE NÀY làm registry duy nhất: mỗi check có
     `code`, `mo_ta`, `sql`, `ky_vong`, `kieu` (eq/ge/gt/le/info), `nhom`.
  2. `scripts/02_verify_data.py`  -> wrapper mỏng, import registry.
  3. `scripts/build_verify_sql.py` -> SINH `02_verify.sql` từ registry
     (nên file SQL luôn khớp với Python).
  4. `scripts/07_unified_verify.py` -> chạy TẤT CẢ check, xuất báo cáo
     duy nhất (JSON + Markdown), exit code khác 0 nếu có check FAIL.

Cấu trúc một check:
    Check(code="A1", nhom="IMPORT", mo_ta="categories = 9",
         sql="SELECT COUNT(*) FROM tenant.categories",
         ky_vong="9", kieu="eq")

Kieu so sanh:
    "eq"   -> bang ky_vong
    "ge"   -> >= ky_vong
    "gt"   -> >  ky_vong
    "le"   -> <= ky_vong
    "info" -> chi xuat, khong fail
"""
from __future__ import annotations

from dataclasses import dataclass

# Nhom check
NHOM_IMPORT = "IMPORT"    # Tinh trang du lieu import (A1..A22)
NHOM_DELIVERY = "DELIVERY"    # schema nghiep vu giao hang (D1..D12)
NHOM_OPS = "OPS"  # anti-fraud / ton kho / EOD / hardening


@dataclass(frozen=True)
class Check:
    code: str
    nhom: str
    mo_ta: str
    sql: str
    ky_vong: str = "0"
    kieu: str = "eq"
    # Nhom D10/D11 dem loi -> 0 la PASS (gia tri 0 khop "ky_vong" roi)
    la_dem_loi: bool = False


# ===================================================================
# NHOM IMPORT - TINH TRANG DU LIEU IMPORT
# ===================================================================
CHECKS_IMPORT: list[Check] = [
    Check("A1", NHOM_IMPORT, "categories = 9",
          "SELECT COUNT(*) FROM tenant.categories", "9", "eq"),
    Check("A2", NHOM_IMPORT, "products hop le (141 SKU that tu RDT, khong rac)",
          """SELECT COUNT(*) FROM tenant.products
             WHERE sku <> 'RD-FACTORY-AGGREGATED'
               AND sku NOT IN ('10000001','10000002','10000004','10000005',
                               '10000006','10000007','10000008','10000009',
                               '10000010')""", "141", "eq"),
    Check("A3", NHOM_IMPORT, "partners",
          "SELECT COUNT(*) FROM tenant.partners", "2723", "eq"),
    Check("A4", NHOM_IMPORT, "sales_orders",
          "SELECT COUNT(*) FROM tenant.sales_orders", "49844", "eq"),
    Check("A5", NHOM_IMPORT, "sales_order_items",
          "SELECT COUNT(*) FROM tenant.sales_order_items", "176000", "ge"),
    Check("A6", NHOM_IMPORT, "items NULL/AGGREGATED = 0",
          """SELECT COUNT(*) FROM tenant.sales_order_items
             WHERE product_id IS NULL OR product_sku = 'RD-FACTORY-AGGREGATED'""",
          "0", "eq"),
    Check("A7", NHOM_IMPORT, "FK items -> orders = 0",
          """SELECT COUNT(*) FROM tenant.sales_order_items i
             LEFT JOIN tenant.sales_orders o ON o.id = i.sales_order_id
             WHERE o.id IS NULL""", "0", "eq"),
    Check("A8", NHOM_IMPORT, "FK items -> products = 0",
          """SELECT COUNT(*) FROM tenant.sales_order_items i
             LEFT JOIN tenant.products p ON p.id = i.product_id
             WHERE p.id IS NULL""", "0", "eq"),
    Check("A9", NHOM_IMPORT, "FK orders -> partners = 0",
          """SELECT COUNT(*) FROM tenant.sales_orders o
             LEFT JOIN tenant.partners p ON p.id = o.customer_id
             WHERE p.id IS NULL""", "0", "eq"),
    Check("A10", NHOM_IMPORT, "FK orders -> warehouses = 0",
          """SELECT COUNT(*) FROM tenant.sales_orders o
             LEFT JOIN tenant.warehouses w ON w.id = o.warehouse_id
             WHERE w.id IS NULL""", "0", "eq"),
    Check("A11", NHOM_IMPORT, "cong thuc tong don (sai so <= 1) = 0",
          """SELECT COUNT(*) FROM tenant.sales_orders
             WHERE ABS((subtotal - discount_amount + tax_amount) - total_amount) > 1""",
          "0", "eq"),
    Check("A12", NHOM_IMPORT, "items qty > 1e6 (loai loi phinh 1000x) = 0",
          "SELECT COUNT(*) FROM tenant.sales_order_items WHERE ordered_qty > 1000000",
          "0", "eq"),
    Check("A13", NHOM_IMPORT, "audit_logs ton tai",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'audit_logs'""",
          "1", "eq"),
    Check("A14", NHOM_IMPORT, "import_jobs >= 1",
          "SELECT COUNT(*) FROM tenant.import_jobs", "1", "ge"),
    Check("A15", NHOM_IMPORT, "price_lists > 0",
          "SELECT COUNT(*) FROM tenant.price_lists", "1", "gt"),
    Check("A16", NHOM_IMPORT, "temporal tables >= 5",
          "SELECT COUNT(*) FROM sys.tables WHERE temporal_type = 2", "5", "ge"),
    Check("A17", NHOM_IMPORT, "trung order_number = 0",
          """SELECT COUNT(*) FROM (SELECT order_number FROM tenant.sales_orders
             GROUP BY order_number HAVING COUNT(*) > 1) d""", "0", "eq"),
    Check("A18", NHOM_IMPORT, "trung sku = 0",
          """SELECT COUNT(*) FROM (SELECT sku FROM tenant.products
             GROUP BY sku HAVING COUNT(*) > 1) d""", "0", "eq"),
    Check("A19", NHOM_IMPORT, "trung partner_code = 0",
          """SELECT COUNT(*) FROM (SELECT partner_code FROM tenant.partners
             GROUP BY partner_code HAVING COUNT(*) > 1) d""", "0", "eq"),
    Check("A20", NHOM_IMPORT, "don CANCELLED (thong tin)",
          "SELECT COUNT(*) FROM tenant.sales_orders WHERE status = 'CANCELLED'",
          "0", "info"),
    Check("A21", NHOM_IMPORT, "import_errors da ghi (thong tin)",
          "SELECT COUNT(*) FROM tenant.import_errors", "0", "info"),
    Check("A22", NHOM_IMPORT, "doi soat line_total vs total_amount (don lech) = 0",
          """SELECT COUNT(*) FROM (
               SELECT o.id FROM tenant.sales_orders o
               JOIN tenant.sales_order_items i ON i.sales_order_id = o.id
               GROUP BY o.id, o.total_amount, o.tax_amount
               HAVING ABS(SUM(i.line_total) - (o.total_amount - o.tax_amount)) > 1
           ) d""", "0", "eq"),
]

# ===================================================================
# NHOM DELIVERY: SCHEMA NGHIEVU GIAO HANG
# ===================================================================
CHECKS_DELIVERY: list[Check] = [
    Check("D1", NHOM_DELIVERY, "5 bang nghiep vu giao hang ton tai",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = 'tenant'
               AND TABLE_NAME IN ('delivery_trips','delivery_trip_orders',
                                  'delivery_pod','pod_items','pod_attachments')""",
          "5", "eq"),
    Check("D2", NHOM_DELIVERY, "UQ_delivery_pod_client (chong double-POD)",
          """SELECT COUNT(*) FROM sys.key_constraints
             WHERE name = 'UQ_delivery_pod_client' AND type = 'UQ'""", "1", "eq"),
    Check("D3", NHOM_DELIVERY, "delivery_pod co cot GPS + failure_reason",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'delivery_pod'
               AND COLUMN_NAME IN ('delivery_latitude','delivery_longitude',
                                   'delivery_gps_accuracy_m','distance_to_partner_m',
                                   'gps_captured_at','failure_reason','gps_gate_result',
                                   'time_guard_result')""", "8", "eq"),
    Check("D4", NHOM_DELIVERY, "delivery_trips co total_current_debt (DB-C7)",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'delivery_trips'
               AND COLUMN_NAME = 'total_current_debt'""", "1", "eq"),
    Check("D5", NHOM_DELIVERY, "partners co 3 cot cong no (DB-C2)",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'partners'
               AND COLUMN_NAME IN ('current_debt','old_debt','old_debt_date')""",
          "3", "eq"),
    Check("D6", NHOM_DELIVERY, "partners co 3 cot geocoding (DB-C10)",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'partners'
               AND COLUMN_NAME IN ('geocoding_confidence','last_geocoded_at','geocoded_address')""",
          "3", "eq"),
    Check("D7", NHOM_DELIVERY, "sales_orders co old_debt_amount + failure (DB-C3/C11)",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'sales_orders'
               AND COLUMN_NAME IN ('old_debt_amount','failure_count','failure_reason')""",
          "3", "eq"),
    Check("D8", NHOM_DELIVERY, "products co barcode + nguong ton kho ",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'products'
               AND COLUMN_NAME IN ('barcode','min_stock','max_stock','reorder_point')""",
          "4", "eq"),
    Check("D9", NHOM_DELIVERY, "CHK_stockledger_type ton tai (DB-C6)",
          """SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CHK_stockledger_type'""", "1", "eq"),
    Check("D10", NHOM_DELIVERY, "FK pod_items -> delivery_pod dung",
          """SELECT COUNT(*) FROM (
               SELECT i.id FROM tenant.pod_items i
               LEFT JOIN tenant.delivery_pod p ON p.id = i.pod_id
               WHERE p.id IS NULL) d""", "0", "eq", la_dem_loi=True),
    Check("D11", NHOM_DELIVERY, "FK delivery_pod -> partners dung",
          """SELECT COUNT(*) FROM (
               SELECT x.id FROM tenant.delivery_pod x
               LEFT JOIN tenant.partners p ON p.id = x.partner_id
               WHERE p.id IS NULL) d""", "0", "eq", la_dem_loi=True),
    Check("D12", NHOM_DELIVERY, "cong no da nap: so KH old_debt <> 0",
          "SELECT COUNT(*) FROM tenant.partners WHERE old_debt <> 0", "480", "ge"),
]

# ===================================================================
# NHOM OPS: ANTI-FRAUD / TON KHO / EOD / HARDENING
# ===================================================================
CHECKS_OPS: list[Check] = [
    # --- Idempotency ---
    Check("OPS-01", NHOM_OPS, "UNIQUE index client_pod_id ton tai",
          """SELECT COUNT(*) FROM sys.indexes
             WHERE name = 'UQ_delivery_pod_client'
               AND object_id = OBJECT_ID('tenant.delivery_pod')
               AND is_unique = 1""", "1", "eq"),
    Check("OPS-02", NHOM_OPS, "khong co POD trung client_pod_id",
          """SELECT COUNT(*) FROM (
               SELECT client_pod_id FROM tenant.delivery_pod
               GROUP BY client_pod_id HAVING COUNT(*) > 1) d""", "0", "eq",
          la_dem_loi=True),
    # --- Anti-fraud ---
    Check("OPS-03", NHOM_OPS, "CHECK gps_gate_result hop le",
          """SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_delivery_pod_gps_gate_result'
               AND parent_object_id = OBJECT_ID('tenant.delivery_pod')""",
          "1", "eq"),
    Check("OPS-04", NHOM_OPS, "CHECK time_guard_result hop le",
          """SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_delivery_pod_time_guard_result'
               AND parent_object_id = OBJECT_ID('tenant.delivery_pod')""",
          "1", "eq"),
    Check("OPS-05", NHOM_OPS, "moi POD deu co time_guard_result",
          """SELECT COUNT(*) FROM tenant.delivery_pod
             WHERE time_guard_result IS NULL""", "0", "eq", la_dem_loi=True),
    # --- / Ton kho ---
    Check("OPS-06", NHOM_OPS, "khong con ton am trong stock_ledger",
          """SELECT COUNT(*) FROM (
               SELECT warehouse_id, product_id, uom_code
               FROM tenant.stock_ledger
               GROUP BY warehouse_id, product_id, uom_code
               HAVING SUM(quantity_in - quantity_out) < 0) d""",
          "0", "eq", la_dem_loi=True),
    Check("OPS-07", NHOM_OPS, "khong con ton am trong inventory_balances",
          """SELECT COUNT(*) FROM tenant.inventory_balances
             WHERE quantity_on_hand < 0""", "0", "eq", la_dem_loi=True),
    Check("OPS-08", NHOM_OPS, "ledger == balances (doi soat song song)",
          """SELECT COUNT(*) FROM (
               SELECT warehouse_id, product_id, uom_code,
                      SUM(quantity_in - quantity_out) AS net
               FROM tenant.stock_ledger
               GROUP BY warehouse_id, product_id, uom_code) l
             FULL JOIN tenant.inventory_balances b
               ON  b.warehouse_id = l.warehouse_id
               AND b.product_id   = l.product_id
               AND b.uom_code     = l.uom_code
             WHERE ISNULL(l.net, 0) <> ISNULL(b.quantity_on_hand, 0)""",
          "0", "eq", la_dem_loi=True),
    Check("OPS-09", NHOM_OPS, "CHECK average_cost >= 0 ton tai",
          """SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_inventory_balance_avg_cost'
               AND parent_object_id = OBJECT_ID('tenant.inventory_balances')""",
          "1", "eq"),
    Check("OPS-10", NHOM_OPS, "trigger ton kho dang bat",
          """SELECT COUNT(*) FROM sys.triggers
             WHERE parent_id = OBJECT_ID('tenant.stock_ledger')
               AND is_disabled = 0""", "1", "eq"),
    Check("OPS-11", NHOM_OPS, "co but toan SALE trong stock_ledger",
          """SELECT COUNT(*) FROM tenant.stock_ledger
             WHERE transaction_type = 'SALE'""", "1", "ge"),
    Check("OPS-12", NHOM_OPS, "CHECK transaction_type hop le",
          """SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CHK_stockledger_type'""", "1", "eq"),
    # --- EOD ---
    Check("OPS-13", NHOM_OPS, "bang eod_reconciliations ton tai",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'eod_reconciliations'""",
          "1", "eq"),
    Check("OPS-14", NHOM_OPS, "bang eod_reconciliation_items ton tai",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'eod_reconciliation_items'""",
          "1", "eq"),
    Check("OPS-15", NHOM_OPS, "CHECK cash_variance_action hop le",
          """SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_eod_cash_action'
               AND parent_object_id = OBJECT_ID('tenant.eod_reconciliations')""",
          "1", "eq"),
    Check("OPS-16", NHOM_OPS, "CHECK items_variance_action hop le",
          """SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_eod_items_action'
               AND parent_object_id = OBJECT_ID('tenant.eod_reconciliations')""",
          "1", "eq"),
    Check("OPS-17", NHOM_OPS, "CHECK status hop le",
          """SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_eod_status'
               AND parent_object_id = OBJECT_ID('tenant.eod_reconciliations')""",
          "1", "eq"),
    # --- Retention ---
    Check("OPS-18", NHOM_OPS, "procedure purge lich su temporal ton tai",
          """SELECT COUNT(*) FROM sys.objects
             WHERE name = 'sp_purge_sales_orders_history'
               AND type = 'P'""", "1", "eq"),
    # --- Audit ---
    Check("OPS-19", NHOM_OPS, "audit_logs co cot user_id + record_id",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'audit_logs'
               AND COLUMN_NAME IN ('user_id','record_id','action','table_name')""",
          "4", "ge"),
    Check("OPS-20", NHOM_OPS, "audit_logs co cot metadata request",
          """SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'audit_logs'
               AND COLUMN_NAME IN ('request_id','ip_address','old_values',
                                   'new_values','changed_fields')""",
          "5", "ge"),
]


ALL_CHECKS: list[Check] = CHECKS_IMPORT + CHECKS_DELIVERY + CHECKS_OPS


def by_nhom(nhom: str) -> list[Check]:
    return [c for c in ALL_CHECKS if c.nhom == nhom]


def evaluate(check: Check, value) -> str:
    """Tra ve 'PASS' | 'FAIL' | 'INFO'."""
    if check.kieu == "info":
        return "INFO"
    try:
        v = int(value)
        e = int(check.ky_vong)
    except (TypeError, ValueError):
        return "INFO"

    if check.kieu == "eq":
        return "PASS" if v == e else "FAIL"
    if check.kieu == "ge":
        return "PASS" if v >= e else "FAIL"
    if check.kieu == "gt":
        return "PASS" if v > e else "FAIL"
    if check.kieu == "le":
        return "PASS" if v <= e else "FAIL"
    return "INFO"
