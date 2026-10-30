-- ===================================================================
-- MIGRATION LEN DB DANG CHAY (ERP_FMCG)
-- ===================================================================
-- Muc dich: them cot/bang moi vao DB da co du lieu (49.844 don, 2.723 KH)
--           ma KHONG duoc xoa dong du lieu nao.
--
-- File 01_schema.sql (build_schema.py) chi tao bang moi. voi bang da
-- ton tai, ta can ALTER TABLE rieng -> file nay.
--
-- ANSI SETTINGS - BAT BUOC
-- ===================================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

USE ERP_FMCG;
GO

PRINT '=========================================================';
PRINT ' Migration len DB ERP_FMCG hien tai';
PRINT '=========================================================';
GO

-- -------------------------------------------------------------------
-- 1. partners: cong no + geocoding (DB-C2, DB-C10)
-- -------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'tenant.partners') AND name = N'current_debt')
BEGIN
    ALTER TABLE tenant.partners ADD current_debt DECIMAL(15,2) NOT NULL DEFAULT 0;
    PRINT '[OK] partners.current_debt da them';
END
ELSE
    PRINT '[SKIP] partners.current_debt da ton tai';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'tenant.partners') AND name = N'old_debt')
BEGIN
    ALTER TABLE tenant.partners ADD old_debt DECIMAL(15,2) NOT NULL DEFAULT 0;
    PRINT '[OK] partners.old_debt da them';
END
ELSE
    PRINT '[SKIP] partners.old_debt da ton tai';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'tenant.partners') AND name = N'old_debt_date')
BEGIN
    ALTER TABLE tenant.partners ADD old_debt_date DATE NULL;
    PRINT '[OK] partners.old_debt_date da them';
END
ELSE
    PRINT '[SKIP] partners.old_debt_date da ton tai';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'tenant.partners') AND name = N'geocoding_confidence')
BEGIN
    ALTER TABLE tenant.partners ADD geocoding_confidence VARCHAR(10) NULL
        CONSTRAINT CHK_partners_geocoding_conf CHECK (
            geocoding_confidence IN ('LOW','MEDIUM','HIGH','MANUAL'));
    ALTER TABLE tenant.partners ADD last_geocoded_at DATETIME2 NULL;
    ALTER TABLE tenant.partners ADD geocoded_address NVARCHAR(500) NULL;
    PRINT '[OK] partners.geocoding_* da them (3 cot)';
END
ELSE
    PRINT '[SKIP] partners.geocoding_* da ton tai';
GO

-- -------------------------------------------------------------------
-- 2. sales_orders: cong no cu + retry logic (DB-C3, DB-C11)
-- -------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'tenant.sales_orders') AND name = N'old_debt_amount')
BEGIN
    ALTER TABLE tenant.sales_orders ADD old_debt_amount DECIMAL(15,2) NOT NULL DEFAULT 0;
    PRINT '[OK] sales_orders.old_debt_amount da them';
END
ELSE
    PRINT '[SKIP] sales_orders.old_debt_amount da ton tai';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'tenant.sales_orders') AND name = N'failure_count')
BEGIN
    ALTER TABLE tenant.sales_orders ADD failure_count INT NOT NULL DEFAULT 0;
    ALTER TABLE tenant.sales_orders ADD failure_reason VARCHAR(50) NULL
        CONSTRAINT CHK_sales_orders_failure_reason CHECK (
            failure_reason IN ('DOOR_LOCKED','WRONG_ORDER','CUSTOMER_ABSENT','TRAFFIC','OTHER'));
    PRINT '[OK] sales_orders.failure_count/failure_reason da them';
END
ELSE
    PRINT '[SKIP] sales_orders.failure_count/failure_reason da ton tai';
GO

-- -------------------------------------------------------------------
-- 3. products: barcode + nguong ton kho (5A.3 / Q4)
-- -------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'tenant.products') AND name = N'barcode')
BEGIN
    ALTER TABLE tenant.products ADD barcode VARCHAR(50) NULL;
    ALTER TABLE tenant.products ADD min_stock DECIMAL(15,2) NOT NULL DEFAULT 0;
    ALTER TABLE tenant.products ADD max_stock DECIMAL(15,2) NOT NULL DEFAULT 0;
    ALTER TABLE tenant.products ADD reorder_point DECIMAL(15,2) NOT NULL DEFAULT 0;
    PRINT '[OK] products.barcode/min_stock/max_stock/reorder_point da them';
END
ELSE
    PRINT '[SKIP] products.barcode da ton tai';
GO

-- -------------------------------------------------------------------
-- 4. stock_ledger: chuan hoa CHECK transaction_type (DB-C6)
-- -------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CHK_stockledger_type')
BEGIN
    -- Kiem tra truoc khi them: co dong vi pham khong?
    DECLARE @bad INT = (SELECT COUNT(*) FROM tenant.stock_ledger
                        WHERE transaction_type NOT IN ('PURCHASE','SALE','TRANSFER_IN','TRANSFER_OUT',
                                                      'RETURN_IN','RETURN_OUT','ADJUSTMENT','OPENING'));
    IF @bad > 0
    BEGIN
        PRINT '[WARN] Co ' + CAST(@bad AS VARCHAR(10)) + ' dong stock_ledger co transaction_type sai -> KHONG them CHECK. Can sua truoc.';
    END
    ELSE
    BEGIN
        ALTER TABLE tenant.stock_ledger DROP CONSTRAINT CHK_stockledger_type;
        ALTER TABLE tenant.stock_ledger ADD CONSTRAINT CHK_stockledger_type CHECK (
            transaction_type IN ('PURCHASE','SALE','TRANSFER_IN','TRANSFER_OUT',
                                 'RETURN_IN','RETURN_OUT','ADJUSTMENT','OPENING'));
        PRINT '[OK] CHK_stockledger_type da chuan hoa';
    END
END
ELSE
BEGIN
    ALTER TABLE tenant.stock_ledger ADD CONSTRAINT CHK_stockledger_type CHECK (
        transaction_type IN ('PURCHASE','SALE','TRANSFER_IN','TRANSFER_OUT',
                             'RETURN_IN','RETURN_OUT','ADJUSTMENT','OPENING'));
    PRINT '[OK] CHK_stockledger_type da them';
END
GO

-- -------------------------------------------------------------------
-- 5. 5 bang nghiep vu giao hang (noi dung file 07_delivery_tables.sql)
-- -------------------------------------------------------------------
IF OBJECT_ID('tenant.delivery_trips', 'U') IS NULL
BEGIN
    PRINT '[INFO] Chay 07_delivery_tables.sql de tao 5 bang giao hang';
END
ELSE
    PRINT '[SKIP] 5 bang giao hang da ton tai';
GO

-- -------------------------------------------------------------------
-- 6. Nong VARCHAR cho gps_gate_result
--    'SKIPPED_NO_PARTNER_GPS' = 23 ky tu > VARCHAR(20) -> bi truncate
-- -------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'tenant.delivery_pod')
             AND name = N'gps_gate_result'
             AND CHARACTER_MAXIMUM_LENGTH < 30)
BEGIN
    ALTER TABLE tenant.delivery_pod
        ALTER COLUMN gps_gate_result VARCHAR(30) NULL;
    PRINT '[OK] delivery_pod.gps_gate_result da nong tu VARCHAR(20) -> VARCHAR(30)';
END
ELSE
    PRINT '[SKIP] delivery_pod.gps_gate_result da du dai';
GO

PRINT '=========================================================';
PRINT ' HOAN TAT migration';
PRINT ' Buoc tiep theo: chay scripts/03_import_partner_debt.py';
PRINT '=========================================================';
GO
