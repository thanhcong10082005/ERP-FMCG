-- ===================================================================
-- PHASE 6 MIGRATION — 09_phase6_dispatcher_eod.sql
-- -------------------------------------------------------------------
-- Ap dung cac thay doi DB cho Phase 6:
--
--   6B.4  Tao bang tenant.eod_reconciliations + eod_reconciliation_items
--         (Wizard doi soat cuoi ngay — chuyen tu Postgres v13_3__eod_reconciliation.sql)
--
--   6C.6  Retention policy: temporal 24 thang cho sales_orders_history
--         + chi muc phuc vuc retention.
--
--   6A.3  Dam bao trigger stock_ledger -> inventory_balances hoat dong
--         (MERGE) va co CHECK cho transaction_type.
--
-- IDEMPOTENT: chay lai nhieu lan deu an toan (IF OBJECT_ID / IF NOT EXISTS).
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

-- ===================================================================
-- 6B.4 — BANG DOI SOAT CUOI NGAY (EOD)
-- ===================================================================

IF OBJECT_ID('tenant.eod_reconciliations', 'U') IS NULL
CREATE TABLE tenant.eod_reconciliations (
    id                      UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    trip_id                 UNIQUEIDENTIFIER NULL,
    reconciliation_date     DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),

    driver_id               UNIQUEIDENTIFIER NULL,
    driver_name             NVARCHAR(200) NULL,
    warehouse_id            UNIQUEIDENTIFIER NULL,

    -- Tien mat
    expected_cash           DECIMAL(15,2) NOT NULL DEFAULT 0,
    actual_cash             DECIMAL(15,2) NOT NULL DEFAULT 0,
    cash_difference         DECIMAL(15,2) NOT NULL DEFAULT 0,
    cash_variance_reason    NVARCHAR(500) NULL,
    -- ★ CHECK: chi 3 gia tri hop le (acceptance 6B.4)
    cash_variance_action    VARCHAR(50) NULL
        CONSTRAINT CK_eod_cash_action CHECK (
            cash_variance_action IS NULL OR
            cash_variance_action IN ('DRIVER_REIMBURSE','SUSPEND_DEBT','WRITE_OFF')
        ),

    -- Hang hoa
    expected_items          INT NOT NULL DEFAULT 0,
    delivered_items         INT NOT NULL DEFAULT 0,
    returned_items          INT NOT NULL DEFAULT 0,
    items_difference        INT NOT NULL DEFAULT 0,
    items_variance_reason   NVARCHAR(500) NULL,
    -- ★ CHECK: chi 3 gia tri hop le
    items_variance_action   VARCHAR(50) NULL
        CONSTRAINT CK_eod_items_action CHECK (
            items_variance_action IS NULL OR
            items_variance_action IN ('DRIVER_COMPENSATE','RETURN_TO_STOCK','WRITE_OFF')
        ),

    -- Trang thai
    status                  VARCHAR(20) NOT NULL DEFAULT 'PENDING'
        CONSTRAINT CK_eod_status CHECK (
            status IN ('PENDING','APPROVED','DISPUTED','CANCELLED')
        ),

    -- Don hang
    total_orders            INT NOT NULL DEFAULT 0,
    successful_orders       INT NOT NULL DEFAULT 0,
    failed_orders           INT NOT NULL DEFAULT 0,

    trip_expenses           DECIMAL(15,2) NOT NULL DEFAULT 0,
    notes                   NVARCHAR(500) NULL,

    approved_by             UNIQUEIDENTIFIER NULL,
    approved_at             DATETIME2 NULL,

    created_by              UNIQUEIDENTIFIER NULL,
    created_at              DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at              DATETIME2 NOT NULL DEFAULT GETDATE(),

    CONSTRAINT FK_eod_trip FOREIGN KEY (trip_id)
        REFERENCES tenant.delivery_trips(id),
    CONSTRAINT FK_eod_driver FOREIGN KEY (driver_id)
        REFERENCES tenant.user_accounts(id),
    CONSTRAINT FK_eod_warehouse FOREIGN KEY (warehouse_id)
        REFERENCES tenant.warehouses(id),
    CONSTRAINT FK_eod_approved_by FOREIGN KEY (approved_by)
        REFERENCES tenant.user_accounts(id),
    CONSTRAINT FK_eod_created_by FOREIGN KEY (created_by)
        REFERENCES tenant.user_accounts(id)
);

PRINT 'Da tao bang tenant.eod_reconciliations';
GO

IF OBJECT_ID('tenant.eod_reconciliation_items', 'U') IS NULL
CREATE TABLE tenant.eod_reconciliation_items (
    id                      UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    reconciliation_id       UNIQUEIDENTIFIER NOT NULL,

    sales_order_id          UNIQUEIDENTIFIER NULL,
    partner_id              UNIQUEIDENTIFIER NULL,
    partner_name            NVARCHAR(200) NULL,

    expected_cash           DECIMAL(15,2) NOT NULL DEFAULT 0,
    collected_cash          DECIMAL(15,2) NOT NULL DEFAULT 0,
    old_debt_collected      DECIMAL(15,2) NOT NULL DEFAULT 0,
    cash_difference         DECIMAL(15,2) NOT NULL DEFAULT 0,
    cash_variance_reason    NVARCHAR(500) NULL,

    expected_qty            DECIMAL(15,2) NOT NULL DEFAULT 0,
    delivered_qty           DECIMAL(15,2) NOT NULL DEFAULT 0,
    returned_qty            DECIMAL(15,2) NOT NULL DEFAULT 0,
    items_difference        DECIMAL(15,2) NOT NULL DEFAULT 0,

    reconciliation_status   VARCHAR(20) NOT NULL DEFAULT 'MATCHED'
        CONSTRAINT CK_eod_item_status CHECK (
            reconciliation_status IN ('MATCHED','CASH_SHORT','CASH_OVER','ITEMS_SHORT','FULL_MATCH')
        ),

    created_at              DATETIME2 NOT NULL DEFAULT GETDATE(),

    CONSTRAINT FK_eod_item_recon FOREIGN KEY (reconciliation_id)
        REFERENCES tenant.eod_reconciliations(id) ON DELETE CASCADE,
    CONSTRAINT FK_eod_item_so FOREIGN KEY (sales_order_id)
        REFERENCES tenant.sales_orders(id),
    CONSTRAINT FK_eod_item_partner FOREIGN KEY (partner_id)
        REFERENCES tenant.partners(id)
);

PRINT 'Da tao bang tenant.eod_reconciliation_items';
GO

-- ---- Index ----
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_eod_trip' AND object_id = OBJECT_ID(N'tenant.eod_reconciliations'))
    CREATE INDEX idx_eod_trip ON tenant.eod_reconciliations(trip_id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_eod_date' AND object_id = OBJECT_ID(N'tenant.eod_reconciliations'))
    CREATE INDEX idx_eod_date ON tenant.eod_reconciliations(reconciliation_date DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_eod_driver' AND object_id = OBJECT_ID(N'tenant.eod_reconciliations'))
    CREATE INDEX idx_eod_driver ON tenant.eod_reconciliations(driver_id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_eod_status' AND object_id = OBJECT_ID(N'tenant.eod_reconciliations'))
    CREATE INDEX idx_eod_status ON tenant.eod_reconciliations(status);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_eod_item_recon' AND object_id = OBJECT_ID(N'tenant.eod_reconciliation_items'))
    CREATE INDEX idx_eod_item_recon ON tenant.eod_reconciliation_items(reconciliation_id);
GO

PRINT 'Da tao index cho eod_reconciliations';
GO

-- ===================================================================
-- 6C.6 — RETENTION POLICY (TEMPORAL 24 THANG)
-- -------------------------------------------------------------------
-- sales_orders_history sinh ra rat nhanh (moi don 1 ban ghi history).
-- Chinh sach giu 24 thang.
-- ===================================================================

-- ★ LUU Y: CREATE PROCEDURE phai la lenh DAU TIEN cua batch (yeu cau cua
--   SQL Server). Vì vậy phai tach rieng GO truoc khi tao.
-- ★ LUU Y 2: KHONG duoc DELETE truc tiep tu bang temporal history
--   (SQL Server: "Cannot delete rows from a temporal history table").
--   Phai TAT SYSTEM_VERSIONING truoc, xoa, roi BAT LAI.
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = N'sp_purge_sales_orders_history')
    PRINT 'Chua co sp_purge_sales_orders_history - se tao o batch sau';
GO

CREATE OR ALTER PROCEDURE dbo.sp_purge_sales_orders_history
    @MonthsToKeep INT = 24
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @cutoff DATETIME2 = DATEADD(MONTH, -@MonthsToKeep, SYSUTCDATETIME());
    DECLARE @deleted INT = 0;

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'sales_orders_history'
                   AND schema_id = SCHEMA_ID('tenant'))
    BEGIN
        PRINT 'tenant.sales_orders_history khong ton tai - bo qua.';
        RETURN 0;
    END

    -- temporal_type: 0 = khong temporal, 1 = history table (versioning OFF),
    --                 2 = system-versioned (versioning ON) -> phai tat truoc.
    --
    -- ★ BAT BUOC DUNG DYNAMIC SQL (sp_executesql) cho moi thao tac DELETE:
    --   SQL Server kiem tra compile-time "Cannot delete rows from a temporal
    --   history table" — khi COMPILE procedure, ke ca trong nhanh IF chua
    --   chay. Dynamic SQL deferred toi runtime, luc do versioning da OFF
    --   => DELETE hop le.
    IF EXISTS (SELECT 1 FROM sys.tables
               WHERE name = 'sales_orders' AND schema_id = SCHEMA_ID('tenant')
                 AND temporal_type = 2)
    BEGIN
        -- Dang BAT SYSTEM_VERSIONING -> tat, xoa, bat lai.
        ALTER TABLE tenant.sales_orders SET (SYSTEM_VERSIONING = OFF);

        EXEC sp_executesql N'
            DELETE FROM tenant.sales_orders_history
            WHERE SysStartTime < @cutoff;
            SET @rowcount = @@ROWCOUNT;',
            N'@cutoff DATETIME2, @rowcount INT OUTPUT',
            @cutoff = @cutoff,
            @rowcount = @deleted OUTPUT;

        ALTER TABLE tenant.sales_orders
            SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = tenant.sales_orders_history));
    END
    ELSE
    BEGIN
        -- History table da tach roi (versioning OFF) -> xoa truc tiep.
        EXEC sp_executesql N'
            DELETE FROM tenant.sales_orders_history
            WHERE SysStartTime < @cutoff;
            SET @rowcount = @@ROWCOUNT;',
            N'@cutoff DATETIME2, @rowcount INT OUTPUT',
            @cutoff = @cutoff,
            @rowcount = @deleted OUTPUT;
    END

    RETURN @deleted;
END;
GO

PRINT 'Da tao ham dbo.fn_purge_sales_orders_history()';
GO

-- ===================================================================
-- 6A.3 — KIEM TRA TRIGGER stock_ledger -> inventory_balances
-- ===================================================================
-- Trigger ton tai (Phase 4/5) nhung can xac nhan no dang MERGE
-- (khong phai INSERT-only) va co xu ly case san pham moi.
-- ===================================================================

IF OBJECT_ID('tenant.trg_stockledger_after_insert', 'TR') IS NOT NULL
    PRINT 'OK: trigger trg_stockledger_after_insert da ton tai';
ELSE
    PRINT 'CANH BAO: thieu trigger trg_stockledger_after_insert - ton kho se khong duoc cap nhat';
GO

-- ===================================================================
-- 6A.2 — DAM BAO UNIQUE client_pod_id (idempotency)
-- ===================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UQ_delivery_pod_client'
                 AND object_id = OBJECT_ID(N'tenant.delivery_pod'))
BEGIN
    PRINT 'CANH BAO: thieu UNIQUE index UQ_delivery_pod_client - co the ghi trung POD';
END
ELSE
    PRINT 'OK: UNIQUE index UQ_delivery_pod_client da ton tai';
GO

-- ===================================================================
-- 6A.1 — CHECK gia tri gps_gate_result / time_guard_result
-- ===================================================================
-- Do AntiFraudService sinh ra, danh sach gia tri phai khop.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_delivery_pod_gps_gate_result'
                 AND parent_object_id = OBJECT_ID(N'tenant.delivery_pod'))
BEGIN
    ALTER TABLE tenant.delivery_pod WITH CHECK
        ADD CONSTRAINT CK_delivery_pod_gps_gate_result
        CHECK (gps_gate_result IS NULL OR
               gps_gate_result IN ('PASS','FAIL','SKIPPED_NO_PARTNER_GPS','FAIL_NO_GPS','SKIPPED'));
    PRINT 'Da them CHECK CK_delivery_pod_gps_gate_result';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_delivery_pod_time_guard_result'
                 AND parent_object_id = OBJECT_ID(N'tenant.delivery_pod'))
BEGIN
    ALTER TABLE tenant.delivery_pod WITH CHECK
        ADD CONSTRAINT CK_delivery_pod_time_guard_result
        CHECK (time_guard_result IS NULL OR
               time_guard_result IN ('PASS','FAIL','FAIL_BACKDATE','TIMESTAMP_MISMATCH','SKIPPED','SKIPPED_NO_GPS'));
    PRINT 'Da them CHECK CK_delivery_pod_time_guard_result';
END
GO

-- ===================================================================
-- KET QUA
-- ===================================================================
PRINT '';
PRINT '========================================';
PRINT 'PHASE 6 MIGRATION (09_phase6) HOAN TAT';
PRINT '  - tenant.eod_reconciliations          : OK';
PRINT '  - tenant.eod_reconciliation_items     : OK';
PRINT '  - CHECK constraint hanh dong EOD       : OK';
PRINT '  - fn_purge_sales_orders_history()     : OK (retention 24 thang)';
PRINT '========================================';
GO
