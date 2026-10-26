-- ===================================================================
-- ERP-FMCG - KIỂM TRA DỮ LIỆU SAU IMPORT
-- -------------------------------------------------------------------
-- Mục đích : đối soát dữ liệu sau khi chạy scripts/01_full_import_pipeline.py
-- Chạy    : sqlcmd -S ".\SQLEXPRESS" -d "ERP_FMCG" -E -f 65001 -i 02_verify.sql
-- Lưu ý   : -f 65001 BẮT BUỘC (file UTF-8 không BOM, sqlcmd mặc định đọc ANSI)
--            Script KHÔNG có side-effect (chỉ SELECT + PRINT)
-- Encoding: UTF-8 KHÔNG BOM
-- ===================================================================

SET NOCOUNT ON;
GO

PRINT '===================================================================';
PRINT ' ERP-FMCG - BAO CAO KIEM TRA DU LIEU';
PRINT '===================================================================';
GO

-- ===================================================================
-- 1. SO LUONG BAN GHI TUNG BANG
-- ===================================================================
PRINT '';
PRINT '--- 1. SO LUONG BAN GHI ---';
GO

SELECT
    t.name                                                AS bang,
    p.rows                                                AS so_dong,
    CASE
        WHEN t.name = 'categories'          THEN 'A1  ky vong = 9'
        WHEN t.name = 'products'            THEN 'A2  ky vong = 141'
        WHEN t.name = 'partners'            THEN 'A3  ky vong = 2.682'
        WHEN t.name = 'sales_orders'        THEN 'A4  ky vong = 49.844'
        WHEN t.name = 'sales_order_items'   THEN 'A5  ky vong >= 176.000'
        WHEN t.name = 'price_lists'         THEN 'A14 ky vong > 0'
        WHEN t.name = 'import_jobs'         THEN 'A12 ky vong >= 1'
        ELSE ''
    END                                                   AS ky_vong
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
CROSS APPLY (
    SELECT SUM(CASE WHEN i.index_id IN (0, 1) THEN p.rows ELSE 0 END) AS rows
    FROM sys.partitions p
    JOIN sys.indexes i ON i.object_id = p.object_id AND i.index_id = p.index_id
    WHERE p.object_id = t.object_id
) p
WHERE s.name = 'tenant'
  AND t.is_ms_shipped = 0
  AND t.name NOT LIKE '%_history'
ORDER BY p.rows DESC;
GO

-- ===================================================================
-- 2. KIEM TRA TINH TRANG DU LIEU (A1 - A19)
-- ===================================================================
PRINT '';
PRINT '--- 2. KIEM TRA TINH TRANG (A1 - A19) ---';
GO

WITH checks AS (
    SELECT 1 AS ord, 'A1  categories = 9'                        AS kiem_tra,
           CAST((SELECT COUNT(*) FROM tenant.categories) AS nvarchar(50)) AS gia_tri,
           '9'                                                   AS ky_vong
    UNION ALL
    SELECT 2, 'A2  products hop le (141 SKU that tu RDT, khong rac)',
           CAST((SELECT COUNT(*) FROM tenant.products
             WHERE sku <> 'RD-FACTORY-AGGREGATED'
               AND sku NOT IN ('10000001', '10000002', '10000004', '10000005',
                               '10000006', '10000007', '10000008', '10000009',
                               '10000010')) AS nvarchar(50)),
           '141'
    UNION ALL
    SELECT 3, 'A3  partners',
           CAST((SELECT COUNT(*) FROM tenant.partners) AS nvarchar(50)), '2723'
    UNION ALL
    SELECT 4, 'A4  sales_orders',
           CAST((SELECT COUNT(*) FROM tenant.sales_orders) AS nvarchar(50)), '49844'
    UNION ALL
    SELECT 5, 'A5  sales_order_items',
           CAST((SELECT COUNT(*) FROM tenant.sales_order_items) AS nvarchar(50)), '>=176000'
    UNION ALL
    SELECT 6, 'A6  items NULL/AGGREGATED = 0',
           CAST((SELECT COUNT(*) FROM tenant.sales_order_items
             WHERE product_id IS NULL OR product_sku = 'RD-FACTORY-AGGREGATED') AS nvarchar(50)), '0'
    UNION ALL
    SELECT 7, 'A7  FK items -> orders',
           CAST((SELECT COUNT(*) FROM tenant.sales_order_items i
             LEFT JOIN tenant.sales_orders o ON o.id = i.sales_order_id
             WHERE o.id IS NULL) AS nvarchar(50)), '0'
    UNION ALL
    SELECT 8, 'A8  FK items -> products',
           CAST((SELECT COUNT(*) FROM tenant.sales_order_items i
             LEFT JOIN tenant.products p ON p.id = i.product_id
             WHERE p.id IS NULL) AS nvarchar(50)), '0'
    UNION ALL
    SELECT 9, 'A9  FK orders -> partners',
           CAST((SELECT COUNT(*) FROM tenant.sales_orders o
             LEFT JOIN tenant.partners p ON p.id = o.customer_id
             WHERE p.id IS NULL) AS nvarchar(50)), '0'
    UNION ALL
    SELECT 10, 'A10 FK orders -> warehouses',
           CAST((SELECT COUNT(*) FROM tenant.sales_orders o
             LEFT JOIN tenant.warehouses w ON w.id = o.warehouse_id
             WHERE w.id IS NULL) AS nvarchar(50)), '0'
    UNION ALL
    SELECT 11, 'A11 cong thuc tong don (sai so <= 1)',
           CAST((SELECT COUNT(*) FROM tenant.sales_orders
             WHERE ABS((subtotal - discount_amount + tax_amount) - total_amount) > 1) AS nvarchar(50)), '0'
    UNION ALL
    SELECT 12, 'A12 items qty > 1e6 (loai loi phinh 1000x)',
           CAST((SELECT COUNT(*) FROM tenant.sales_order_items WHERE ordered_qty > 1000000) AS nvarchar(50)), '0'
    UNION ALL
    SELECT 13, 'A13 audit_logs ton tai',
           CAST((SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'audit_logs') AS nvarchar(50)), '1'
    UNION ALL
    SELECT 14, 'A14 import_jobs >= 1',
           CAST((SELECT COUNT(*) FROM tenant.import_jobs) AS nvarchar(50)), '>=1'
    UNION ALL
    SELECT 15, 'A15 price_lists > 0',
           CAST((SELECT COUNT(*) FROM tenant.price_lists) AS nvarchar(50)), '>0'
    UNION ALL
    SELECT 16, 'A16 temporal tables >= 5',
           CAST((SELECT COUNT(*) FROM sys.tables WHERE temporal_type = 2) AS nvarchar(50)), '>=5'
    UNION ALL
    SELECT 17, 'A17 trung order_number',
           CAST((SELECT COUNT(*) FROM (SELECT order_number FROM tenant.sales_orders
                                  GROUP BY order_number HAVING COUNT(*) > 1) d) AS nvarchar(50)), '0'
    UNION ALL
    SELECT 18, 'A18 trung sku',
           CAST((SELECT COUNT(*) FROM (SELECT sku FROM tenant.products
                                  GROUP BY sku HAVING COUNT(*) > 1) d) AS nvarchar(50)), '0'
    UNION ALL
    SELECT 19, 'A19 trung partner_code',
           CAST((SELECT COUNT(*) FROM (SELECT partner_code FROM tenant.partners
                                  GROUP BY partner_code HAVING COUNT(*) > 1) d) AS nvarchar(50)), '0'
    UNION ALL
    SELECT 20, 'A20 don CANCELLED',
           CAST((SELECT COUNT(*) FROM tenant.sales_orders WHERE status = 'CANCELLED') AS nvarchar(50)), '~1454'
    UNION ALL
    SELECT 21, 'A21 import_errors da ghi',
           CAST((SELECT COUNT(*) FROM tenant.import_errors) AS nvarchar(50)), '>=0'
    UNION ALL
    SELECT 22, 'A22 doi soat line_total vs total_amount (don lech)',
           CAST((SELECT COUNT(*) FROM (
              SELECT o.id FROM tenant.sales_orders o
              JOIN tenant.sales_order_items i ON i.sales_order_id = o.id
              GROUP BY o.id, o.total_amount, o.tax_amount
              HAVING ABS(SUM(i.line_total) - (o.total_amount - o.tax_amount)) > 1
            ) d) AS nvarchar(50)), '0'
)
SELECT kiem_tra, gia_tri, ky_vong,
       CASE
           WHEN kiem_tra LIKE 'A5%'  AND CAST(gia_tri AS bigint) >= 176000 THEN 'PASS'
           WHEN kiem_tra LIKE 'A14%' AND CAST(gia_tri AS bigint) >= 1     THEN 'PASS'
           WHEN kiem_tra LIKE 'A15%' AND CAST(gia_tri AS bigint) > 0      THEN 'PASS'
           WHEN kiem_tra LIKE 'A16%' AND CAST(gia_tri AS bigint) >= 5     THEN 'PASS'
           WHEN kiem_tra LIKE 'A20%'                                      THEN 'INFO'
           WHEN kiem_tra LIKE 'A21%'                                      THEN 'INFO'
           WHEN gia_tri = ky_vong                                         THEN 'PASS'
           ELSE 'CHECK'
       END AS trang_thai
FROM checks
ORDER BY ord;
GO

-- ===================================================================
-- 3. DOI SOAT TONG TIEN (A9 - tong line_total = tong total_amount)
-- ===================================================================
PRINT '';
PRINT '--- 3. DOI SOAT TONG TIEN ---';
GO

-- Cong thuc: SUM(line_total) = order.subtotal - order.discount = order.total_amount - order.tax_amount
-- (sales_order_items KHONG co cot tax_amount - thue chi o cap don)
SELECT
    COUNT(*)                                                              AS so_don,
    SUM(ABS(so.tong_line_total - so.total_amount + so.tax_amount))      AS tong_sai_so,
    MAX(ABS(so.tong_line_total - so.total_amount + so.tax_amount))      AS sai_so_max,
    SUM(CASE WHEN ABS(so.tong_line_total - so.total_amount + so.tax_amount) > 1
             THEN 1 ELSE 0 END)                                          AS so_don_lech
FROM (
    SELECT o.id, o.total_amount, o.tax_amount,
           SUM(i.line_total) AS tong_line_total
    FROM tenant.sales_orders o
    JOIN tenant.sales_order_items i ON i.sales_order_id = o.id
    GROUP BY o.id, o.total_amount, o.tax_amount
) so;
GO

-- ===================================================================
-- 4. PHAN BO TRANG THAI DON
-- ===================================================================
PRINT '';
PRINT '--- 4. PHAN BO TRANG THAI DON ---';
GO

SELECT status, COUNT(*) AS so_don, SUM(total_amount) AS tong_tien
FROM tenant.sales_orders
GROUP BY status
ORDER BY so_don DESC;
GO

-- ===================================================================
-- 5. TOP 10 DON GIA TRI CAO NHAT
-- ===================================================================
PRINT '';
PRINT '--- 5. TOP 10 DON GIA TRI CAO NHAT ---';
GO

SELECT TOP 10 order_number, order_date, customer_id, status, total_amount
FROM tenant.sales_orders
ORDER BY total_amount DESC;
GO

-- ===================================================================
-- 6. THONG KE LOI IMPORT (theo ma)
-- ===================================================================
PRINT '';
PRINT '--- 6. THONG KE LOI IMPORT ---';
GO

SELECT error_code, severity, COUNT(*) AS so_lan,
       MIN(error_message) AS vi_du
FROM tenant.import_errors
GROUP BY error_code, severity
ORDER BY so_lan DESC;
GO

-- ===================================================================
-- 7. BANG TEMPORAL (lich su)
-- ===================================================================
PRINT '';
PRINT '--- 7. BANG TEMPORAL ---';
GO

SELECT t.name AS bang, t.temporal_type_desc,
       CASE WHEN t.temporal_type = 2 THEN 'TEMPORAL' ELSE 'CURRENT' END AS loai
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'tenant' AND (t.temporal_type = 2 OR t.name LIKE '%_history')
ORDER BY t.name;
GO

PRINT '';
PRINT '===================================================================';
PRINT ' HOAN TAT KIEM TRA. Doc bang "trang_thai" de biet PASS / CHECK.';
PRINT '===================================================================';
GO
