-- ===================================================================
-- ERP-FMCG - KIEM TRA DU LIEU SAU IMPORT (SINH TU verify_registry.py)
-- -------------------------------------------------------------------
-- *** FILE NAY DUOC SINH TU DONG - KHONG SUA TRUC TIEP ***
-- Nguon su that: scripts/verify_registry.py
-- Sinh lai:      python scripts/build_verify_sql.py
-- Chay:          sqlcmd -S ".\SQLEXPRESS" -d "ERP_FMCG" -E -f 65001 -i 02_verify.sql
-- LUU Y:        -f 65001 BAT BUOC (file UTF-8 khong BOM)
--               Script KHONG co side-effect (chi SELECT + PRINT)
-- ===================================================================

SET NOCOUNT ON;
GO

PRINT '===================================================================';
PRINT ' ERP-FMCG - BAO CAO KIEM TRA DU LIEU (BO THONG NHAT - PHASE 6C.3)';
PRINT '===================================================================';
GO

-- -------------------------------------------------------------------
-- 1. BANG TAM TONG HOP CHECK (sinh tu registry)
-- -------------------------------------------------------------------
WITH checks AS (
    SELECT 1 AS ord, 'A1' AS ma, 'A' AS nhom, 'categories = 9' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.categories
           ) AS nvarchar(50)) AS gia_tri,
           '9' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 2 AS ord, 'A2' AS ma, 'A' AS nhom, 'products hop le (141 SKU that tu RDT, khong rac)' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.products
             WHERE sku <> 'RD-FACTORY-AGGREGATED'
             AND sku NOT IN ('10000001','10000002','10000004','10000005',
             '10000006','10000007','10000008','10000009',
             '10000010')
           ) AS nvarchar(50)) AS gia_tri,
           '141' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 3 AS ord, 'A3' AS ma, 'A' AS nhom, 'partners' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.partners
           ) AS nvarchar(50)) AS gia_tri,
           '2723' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 4 AS ord, 'A4' AS ma, 'A' AS nhom, 'sales_orders' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.sales_orders
           ) AS nvarchar(50)) AS gia_tri,
           '49844' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 5 AS ord, 'A5' AS ma, 'A' AS nhom, 'sales_order_items' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.sales_order_items
           ) AS nvarchar(50)) AS gia_tri,
           '176000' AS ky_vong,
           'ge' AS kieu
    UNION ALL
    SELECT 6 AS ord, 'A6' AS ma, 'A' AS nhom, 'items NULL/AGGREGATED = 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.sales_order_items
             WHERE product_id IS NULL OR product_sku = 'RD-FACTORY-AGGREGATED'
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 7 AS ord, 'A7' AS ma, 'A' AS nhom, 'FK items -> orders = 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.sales_order_items i
             LEFT JOIN tenant.sales_orders o ON o.id = i.sales_order_id
             WHERE o.id IS NULL
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 8 AS ord, 'A8' AS ma, 'A' AS nhom, 'FK items -> products = 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.sales_order_items i
             LEFT JOIN tenant.products p ON p.id = i.product_id
             WHERE p.id IS NULL
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 9 AS ord, 'A9' AS ma, 'A' AS nhom, 'FK orders -> partners = 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.sales_orders o
             LEFT JOIN tenant.partners p ON p.id = o.customer_id
             WHERE p.id IS NULL
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 10 AS ord, 'A10' AS ma, 'A' AS nhom, 'FK orders -> warehouses = 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.sales_orders o
             LEFT JOIN tenant.warehouses w ON w.id = o.warehouse_id
             WHERE w.id IS NULL
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 11 AS ord, 'A11' AS ma, 'A' AS nhom, 'cong thuc tong don (sai so <= 1) = 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.sales_orders
             WHERE ABS((subtotal - discount_amount + tax_amount) - total_amount) > 1
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 12 AS ord, 'A12' AS ma, 'A' AS nhom, 'items qty > 1e6 (loai loi phinh 1000x) = 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.sales_order_items WHERE ordered_qty > 1000000
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 13 AS ord, 'A13' AS ma, 'A' AS nhom, 'audit_logs ton tai' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'audit_logs'
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 14 AS ord, 'A14' AS ma, 'A' AS nhom, 'import_jobs >= 1' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.import_jobs
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'ge' AS kieu
    UNION ALL
    SELECT 15 AS ord, 'A15' AS ma, 'A' AS nhom, 'price_lists > 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.price_lists
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'gt' AS kieu
    UNION ALL
    SELECT 16 AS ord, 'A16' AS ma, 'A' AS nhom, 'temporal tables >= 5' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.tables WHERE temporal_type = 2
           ) AS nvarchar(50)) AS gia_tri,
           '5' AS ky_vong,
           'ge' AS kieu
    UNION ALL
    SELECT 17 AS ord, 'A17' AS ma, 'A' AS nhom, 'trung order_number = 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM (SELECT order_number FROM tenant.sales_orders
             GROUP BY order_number HAVING COUNT(*) > 1) d
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 18 AS ord, 'A18' AS ma, 'A' AS nhom, 'trung sku = 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM (SELECT sku FROM tenant.products
             GROUP BY sku HAVING COUNT(*) > 1) d
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 19 AS ord, 'A19' AS ma, 'A' AS nhom, 'trung partner_code = 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM (SELECT partner_code FROM tenant.partners
             GROUP BY partner_code HAVING COUNT(*) > 1) d
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 20 AS ord, 'A20' AS ma, 'A' AS nhom, 'don CANCELLED (thong tin)' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.sales_orders WHERE status = 'CANCELLED'
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'info' AS kieu
    UNION ALL
    SELECT 21 AS ord, 'A21' AS ma, 'A' AS nhom, 'import_errors da ghi (thong tin)' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.import_errors
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'info' AS kieu
    UNION ALL
    SELECT 22 AS ord, 'A22' AS ma, 'A' AS nhom, 'doi soat line_total vs total_amount (don lech) = 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM (
             SELECT o.id FROM tenant.sales_orders o
             JOIN tenant.sales_order_items i ON i.sales_order_id = o.id
             GROUP BY o.id, o.total_amount, o.tax_amount
             HAVING ABS(SUM(i.line_total) - (o.total_amount - o.tax_amount)) > 1
             ) d
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 23 AS ord, 'D1' AS ma, 'D' AS nhom, '5 bang nghiep vu giao hang ton tai' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = 'tenant'
             AND TABLE_NAME IN ('delivery_trips','delivery_trip_orders',
             'delivery_pod','pod_items','pod_attachments')
           ) AS nvarchar(50)) AS gia_tri,
           '5' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 24 AS ord, 'D2' AS ma, 'D' AS nhom, 'UQ_delivery_pod_client (chong double-POD)' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.key_constraints
             WHERE name = 'UQ_delivery_pod_client' AND type = 'UQ'
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 25 AS ord, 'D3' AS ma, 'D' AS nhom, 'delivery_pod co cot GPS + failure_reason' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'delivery_pod'
             AND COLUMN_NAME IN ('delivery_latitude','delivery_longitude',
             'delivery_gps_accuracy_m','distance_to_partner_m',
             'gps_captured_at','failure_reason','gps_gate_result',
             'time_guard_result')
           ) AS nvarchar(50)) AS gia_tri,
           '8' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 26 AS ord, 'D4' AS ma, 'D' AS nhom, 'delivery_trips co total_current_debt (DB-C7)' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'delivery_trips'
             AND COLUMN_NAME = 'total_current_debt'
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 27 AS ord, 'D5' AS ma, 'D' AS nhom, 'partners co 3 cot cong no (DB-C2)' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'partners'
             AND COLUMN_NAME IN ('current_debt','old_debt','old_debt_date')
           ) AS nvarchar(50)) AS gia_tri,
           '3' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 28 AS ord, 'D6' AS ma, 'D' AS nhom, 'partners co 3 cot geocoding (DB-C10)' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'partners'
             AND COLUMN_NAME IN ('geocoding_confidence','last_geocoded_at','geocoded_address')
           ) AS nvarchar(50)) AS gia_tri,
           '3' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 29 AS ord, 'D7' AS ma, 'D' AS nhom, 'sales_orders co old_debt_amount + failure (DB-C3/C11)' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'sales_orders'
             AND COLUMN_NAME IN ('old_debt_amount','failure_count','failure_reason')
           ) AS nvarchar(50)) AS gia_tri,
           '3' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 30 AS ord, 'D8' AS ma, 'D' AS nhom, 'products co barcode + nguong ton kho (5A.3)' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'products'
             AND COLUMN_NAME IN ('barcode','min_stock','max_stock','reorder_point')
           ) AS nvarchar(50)) AS gia_tri,
           '4' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 31 AS ord, 'D9' AS ma, 'D' AS nhom, 'CHK_stockledger_type ton tai (DB-C6)' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CHK_stockledger_type'
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 32 AS ord, 'D10' AS ma, 'D' AS nhom, 'FK pod_items -> delivery_pod dung' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM (
             SELECT i.id FROM tenant.pod_items i
             LEFT JOIN tenant.delivery_pod p ON p.id = i.pod_id
             WHERE p.id IS NULL) d
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 33 AS ord, 'D11' AS ma, 'D' AS nhom, 'FK delivery_pod -> partners dung' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM (
             SELECT x.id FROM tenant.delivery_pod x
             LEFT JOIN tenant.partners p ON p.id = x.partner_id
             WHERE p.id IS NULL) d
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 34 AS ord, 'D12' AS ma, 'D' AS nhom, 'cong no da nap: so KH old_debt <> 0' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.partners WHERE old_debt <> 0
           ) AS nvarchar(50)) AS gia_tri,
           '480' AS ky_vong,
           'ge' AS kieu
    UNION ALL
    SELECT 35 AS ord, 'P6-01' AS ma, 'P6' AS nhom, '6A.2 UNIQUE index client_pod_id ton tai' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.indexes
             WHERE name = 'UQ_delivery_pod_client'
             AND object_id = OBJECT_ID('tenant.delivery_pod')
             AND is_unique = 1
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 36 AS ord, 'P6-02' AS ma, 'P6' AS nhom, '6A.2 khong co POD trung client_pod_id' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM (
             SELECT client_pod_id FROM tenant.delivery_pod
             GROUP BY client_pod_id HAVING COUNT(*) > 1) d
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 37 AS ord, 'P6-03' AS ma, 'P6' AS nhom, '6A.1 CHECK gps_gate_result hop le' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_delivery_pod_gps_gate_result'
             AND parent_object_id = OBJECT_ID('tenant.delivery_pod')
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 38 AS ord, 'P6-04' AS ma, 'P6' AS nhom, '6A.1 CHECK time_guard_result hop le' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_delivery_pod_time_guard_result'
             AND parent_object_id = OBJECT_ID('tenant.delivery_pod')
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 39 AS ord, 'P6-05' AS ma, 'P6' AS nhom, '6A.1 moi POD deu co time_guard_result' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.delivery_pod
             WHERE time_guard_result IS NULL
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 40 AS ord, 'P6-06' AS ma, 'P6' AS nhom, '6A.3 khong con ton am trong stock_ledger' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM (
             SELECT warehouse_id, product_id, uom_code
             FROM tenant.stock_ledger
             GROUP BY warehouse_id, product_id, uom_code
             HAVING SUM(quantity_in - quantity_out) < 0) d
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 41 AS ord, 'P6-07' AS ma, 'P6' AS nhom, '6A.3 khong con ton am trong inventory_balances' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.inventory_balances
             WHERE quantity_on_hand < 0
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 42 AS ord, 'P6-08' AS ma, 'P6' AS nhom, '6A.3 ledger == balances (doi soat song song)' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM (
             SELECT warehouse_id, product_id, uom_code,
             SUM(quantity_in - quantity_out) AS net
             FROM tenant.stock_ledger
             GROUP BY warehouse_id, product_id, uom_code) l
             FULL JOIN tenant.inventory_balances b
             ON  b.warehouse_id = l.warehouse_id
             AND b.product_id   = l.product_id
             AND b.uom_code     = l.uom_code
             WHERE ISNULL(l.net, 0) <> ISNULL(b.quantity_on_hand, 0)
           ) AS nvarchar(50)) AS gia_tri,
           '0' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 43 AS ord, 'P6-09' AS ma, 'P6' AS nhom, '6A.3 CHECK average_cost >= 0 ton tai' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_inventory_balance_avg_cost'
             AND parent_object_id = OBJECT_ID('tenant.inventory_balances')
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 44 AS ord, 'P6-10' AS ma, 'P6' AS nhom, '6A.3 trigger ton kho dang bat' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.triggers
             WHERE parent_id = OBJECT_ID('tenant.stock_ledger')
             AND is_disabled = 0
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 45 AS ord, 'P6-11' AS ma, 'P6' AS nhom, '6A.4 co but toan SALE trong stock_ledger' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM tenant.stock_ledger
             WHERE transaction_type = 'SALE'
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'ge' AS kieu
    UNION ALL
    SELECT 46 AS ord, 'P6-12' AS ma, 'P6' AS nhom, '6A.4 CHECK transaction_type hop le' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CHK_stockledger_type'
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 47 AS ord, 'P6-13' AS ma, 'P6' AS nhom, '6B.4 bang eod_reconciliations ton tai' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'eod_reconciliations'
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 48 AS ord, 'P6-14' AS ma, 'P6' AS nhom, '6B.4 bang eod_reconciliation_items ton tai' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'eod_reconciliation_items'
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 49 AS ord, 'P6-15' AS ma, 'P6' AS nhom, '6B.4 CHECK cash_variance_action hop le' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_eod_cash_action'
             AND parent_object_id = OBJECT_ID('tenant.eod_reconciliations')
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 50 AS ord, 'P6-16' AS ma, 'P6' AS nhom, '6B.4 CHECK items_variance_action hop le' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_eod_items_action'
             AND parent_object_id = OBJECT_ID('tenant.eod_reconciliations')
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 51 AS ord, 'P6-17' AS ma, 'P6' AS nhom, '6B.4 CHECK status hop le' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.check_constraints
             WHERE name = 'CK_eod_status'
             AND parent_object_id = OBJECT_ID('tenant.eod_reconciliations')
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 52 AS ord, 'P6-18' AS ma, 'P6' AS nhom, '6C.6 procedure purge lich su temporal ton tai' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM sys.objects
             WHERE name = 'sp_purge_sales_orders_history'
             AND type = 'P'
           ) AS nvarchar(50)) AS gia_tri,
           '1' AS ky_vong,
           'eq' AS kieu
    UNION ALL
    SELECT 53 AS ord, 'P6-19' AS ma, 'P6' AS nhom, '6A.6 audit_logs co cot user_id + record_id' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'audit_logs'
             AND COLUMN_NAME IN ('user_id','record_id','action','table_name')
           ) AS nvarchar(50)) AS gia_tri,
           '4' AS ky_vong,
           'ge' AS kieu
    UNION ALL
    SELECT 54 AS ord, 'P6-20' AS ma, 'P6' AS nhom, '6A.6 audit_logs co cot metadata request' AS kiem_tra,
           CAST((
             SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'audit_logs'
             AND COLUMN_NAME IN ('request_id','ip_address','old_values',
             'new_values','changed_fields')
           ) AS nvarchar(50)) AS gia_tri,
           '5' AS ky_vong,
           'ge' AS kieu
)
SELECT ma, nhom, kiem_tra, gia_tri, ky_vong,
       CASE
           WHEN kieu = 'info' THEN 'INFO'
           WHEN kieu = 'eq' AND CAST(gia_tri AS bigint) = CAST(ky_vong AS bigint) THEN 'PASS'
           WHEN kieu = 'ge' AND CAST(gia_tri AS bigint) >= CAST(ky_vong AS bigint) THEN 'PASS'
           WHEN kieu = 'gt' AND CAST(gia_tri AS bigint) >  CAST(ky_vong AS bigint) THEN 'PASS'
           WHEN kieu = 'le' AND CAST(gia_tri AS bigint) <= CAST(ky_vong AS bigint) THEN 'PASS'
           ELSE 'FAIL'
       END AS trang_thai
FROM checks
ORDER BY ma;
GO

-- -------------------------------------------------------------------
-- 2. TONG HOP TRANG THAI
-- -------------------------------------------------------------------
SELECT nhom, trang_thai, COUNT(*) AS so_check
FROM (
    SELECT ma, nhom, gia_tri, ky_vong,
           CASE
               WHEN kieu = 'info' THEN 'INFO'
               WHEN kieu = 'eq' AND CAST(gia_tri AS bigint) = CAST(ky_vong AS bigint) THEN 'PASS'
               WHEN kieu = 'ge' AND CAST(gia_tri AS bigint) >= CAST(ky_vong AS bigint) THEN 'PASS'
               WHEN kieu = 'gt' AND CAST(gia_tri AS bigint) >  CAST(ky_vong AS bigint) THEN 'PASS'
               WHEN kieu = 'le' AND CAST(gia_tri AS bigint) <= CAST(ky_vong AS bigint) THEN 'PASS'
               ELSE 'FAIL'
           END AS trang_thai
    FROM checks
) t
GROUP BY nhom, trang_thai
ORDER BY nhom, trang_thai;
GO

PRINT '';
PRINT 'HOAN TAT KIEM TRA. Doc bang "trang_thai" de biet PASS / FAIL.';
GO
