-- ===================================================================
-- ANSI SETTINGS - BAT BUOC (F2)
-- CREATE NONCLUSTERED COLUMNSTORE INDEX / filtered index yeu cau
-- cac thiet bi sau. Neu thieu -> Msg 1934 / Msg 1934.
-- ===================================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- ===================================================================
-- TEMPORAL TABLES + AUDIT LOG
-- Bật System-Versioned Temporal Tables cho các bảng cần audit
-- ===================================================================

USE ERP_FMCG;
GO

-- ===================================================================
-- 1. Kiểm tra và bật Temporal Table cho PARTNERS
-- ===================================================================
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'partners' AND schema_id = SCHEMA_ID('tenant'))
   AND NOT EXISTS (
       SELECT 1 FROM sys.tables t
       JOIN sys.columns c1 ON t.object_id = c1.object_id AND c1.name = 'SysStartTime'
       JOIN sys.columns c2 ON t.object_id = c2.object_id AND c2.name = 'SysEndTime'
       WHERE t.name = 'partners' AND SCHEMA_NAME(t.schema_id) = 'tenant'
   )
BEGIN
    -- Add temporal columns if missing
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('[tenant].[partners]') AND name = 'SysStartTime')
    BEGIN
        ALTER TABLE [tenant].[partners]
        ADD
            SysStartTime DATETIME2 GENERATED ALWAYS AS ROW START HIDDEN
                CONSTRAINT DF_partners_SysStart DEFAULT SYSUTCDATETIME(),
            SysEndTime DATETIME2 GENERATED ALWAYS AS ROW END HIDDEN
                CONSTRAINT DF_partners_SysEnd DEFAULT CONVERT(DATETIME2, '9999-12-31 23:59:59.9999999'),
            PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime);
    END

    ALTER TABLE [tenant].[partners]
    SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [tenant].[partners_history]));

    PRINT '✅ Enabled temporal table for partners';
END;
GO

-- ===================================================================
-- 2. PRODUCTS - Temporal
-- ===================================================================
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'products' AND schema_id = SCHEMA_ID('tenant'))
   AND NOT EXISTS (
       SELECT 1 FROM sys.tables t
       JOIN sys.columns c1 ON t.object_id = c1.object_id AND c1.name = 'SysStartTime'
       WHERE t.name = 'products' AND SCHEMA_NAME(t.schema_id) = 'tenant'
   )
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('[tenant].[products]') AND name = 'SysStartTime')
    BEGIN
        ALTER TABLE [tenant].[products]
        ADD
            SysStartTime DATETIME2 GENERATED ALWAYS AS ROW START HIDDEN
                CONSTRAINT DF_products_SysStart DEFAULT SYSUTCDATETIME(),
            SysEndTime DATETIME2 GENERATED ALWAYS AS ROW END HIDDEN
                CONSTRAINT DF_products_SysEnd DEFAULT CONVERT(DATETIME2, '9999-12-31 23:59:59.9999999'),
            PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime);
    END

    ALTER TABLE [tenant].[products]
    SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [tenant].[products_history]));

    PRINT '✅ Enabled temporal table for products';
END;
GO

-- ===================================================================
-- 3. SALES_ORDERS - Temporal
-- ===================================================================
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'sales_orders' AND schema_id = SCHEMA_ID('tenant'))
   AND NOT EXISTS (
       SELECT 1 FROM sys.tables t
       JOIN sys.columns c1 ON t.object_id = c1.object_id AND c1.name = 'SysStartTime'
       WHERE t.name = 'sales_orders' AND SCHEMA_NAME(t.schema_id) = 'tenant'
   )
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('[tenant].[sales_orders]') AND name = 'SysStartTime')
    BEGIN
        ALTER TABLE [tenant].[sales_orders]
        ADD
            SysStartTime DATETIME2 GENERATED ALWAYS AS ROW START HIDDEN
                CONSTRAINT DF_sales_orders_SysStart DEFAULT SYSUTCDATETIME(),
            SysEndTime DATETIME2 GENERATED ALWAYS AS ROW END HIDDEN
                CONSTRAINT DF_sales_orders_SysEnd DEFAULT CONVERT(DATETIME2, '9999-12-31 23:59:59.9999999'),
            PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime);
    END

    ALTER TABLE [tenant].[sales_orders]
    SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [tenant].[sales_orders_history]));

    PRINT '✅ Enabled temporal table for sales_orders';
END;
GO

-- ===================================================================
-- 4. PRICE_LISTS - Temporal
-- ===================================================================
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'price_lists' AND schema_id = SCHEMA_ID('tenant'))
   AND NOT EXISTS (
       SELECT 1 FROM sys.tables t
       JOIN sys.columns c1 ON t.object_id = c1.object_id AND c1.name = 'SysStartTime'
       WHERE t.name = 'price_lists' AND SCHEMA_NAME(t.schema_id) = 'tenant'
   )
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('[tenant].[price_lists]') AND name = 'SysStartTime')
    BEGIN
        ALTER TABLE [tenant].[price_lists]
        ADD
            SysStartTime DATETIME2 GENERATED ALWAYS AS ROW START HIDDEN
                CONSTRAINT DF_price_lists_SysStart DEFAULT SYSUTCDATETIME(),
            SysEndTime DATETIME2 GENERATED ALWAYS AS ROW END HIDDEN
                CONSTRAINT DF_price_lists_SysEnd DEFAULT CONVERT(DATETIME2, '9999-12-31 23:59:59.9999999'),
            PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime);
    END

    ALTER TABLE [tenant].[price_lists]
    SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [tenant].[price_lists_history]));

    PRINT '✅ Enabled temporal table for price_lists';
END;
GO

-- ===================================================================
-- 5. AUDIT LOGS Table
-- ===================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'audit_logs' AND schema_id = SCHEMA_ID('tenant'))
BEGIN
    CREATE TABLE [tenant].[audit_logs] (
        id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

        -- Target record
        table_name      NVARCHAR(100) NOT NULL,
        record_id       UNIQUEIDENTIFIER NOT NULL,

        -- Action
        action          NVARCHAR(20) NOT NULL,  -- INSERT, UPDATE, DELETE, RESTORE
        operation       NVARCHAR(50),           -- CREATE_ORDER, UPDATE_PRICE, ...

        -- Changes
        old_values      NVARCHAR(MAX),           -- JSON snapshot
        new_values      NVARCHAR(MAX),           -- JSON snapshot
        changed_fields  NVARCHAR(MAX),           -- JSON array of field names

        -- Context
        user_id         UNIQUEIDENTIFIER,
        user_name       NVARCHAR(255),
        source          NVARCHAR(50),            -- API, IMPORT, BATCH, SYSTEM

        -- Temporal context
        valid_from      DATETIME2,
        valid_to        DATETIME2,

        request_id      NVARCHAR(100),
        ip_address      NVARCHAR(50),

        notes           NVARCHAR(500),

        created_at      DATETIME2 DEFAULT SYSUTCDATETIME()
    );

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_audit_table_record' AND object_id = OBJECT_ID(N'[tenant].[audit_logs]'))
    CREATE INDEX idx_audit_table_record ON [tenant].[audit_logs](table_name, record_id, created_at DESC);
ELSE
    PRINT 'Bỏ qua index idx_audit_table_record (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_audit_user' AND object_id = OBJECT_ID(N'[tenant].[audit_logs]'))
    CREATE INDEX idx_audit_user ON [tenant].[audit_logs](user_id, created_at DESC);
ELSE
    PRINT 'Bỏ qua index idx_audit_user (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_audit_action' AND object_id = OBJECT_ID(N'[tenant].[audit_logs]'))
    CREATE INDEX idx_audit_action ON [tenant].[audit_logs](action, created_at DESC);
ELSE
    PRINT 'Bỏ qua index idx_audit_action (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_audit_source' AND object_id = OBJECT_ID(N'[tenant].[audit_logs]'))
    CREATE INDEX idx_audit_source ON [tenant].[audit_logs](source, created_at DESC);
ELSE
    PRINT 'Bỏ qua index idx_audit_source (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_audit_created' AND object_id = OBJECT_ID(N'[tenant].[audit_logs]'))
    CREATE INDEX idx_audit_created ON [tenant].[audit_logs](created_at DESC);
ELSE
    PRINT 'Bỏ qua index idx_audit_created (đã tồn tại)';

    PRINT '✅ Created audit_logs table';
END;
GO

-- ===================================================================
-- 6. Helper views cho history queries
-- ===================================================================
IF OBJECT_ID('[tenant].[vw_partner_history]', 'V') IS NOT NULL DROP VIEW [tenant].[vw_partner_history];
GO

CREATE VIEW [tenant].[vw_partner_history] AS
SELECT
    p.id,
    p.partner_code,
    p.partner_name,
    p.phone,
    p.email,
    p.address,
    p.route_code,
    p.credit_limit,
    p.is_active,
    p.updated_at,
    p.SysStartTime AS ValidFrom,
    p.SysEndTime AS ValidTo,
    CASE WHEN p.SysEndTime = '9999-12-31 23:59:59.9999999' THEN 1 ELSE 0 END AS IsCurrent
FROM [tenant].[partners] FOR SYSTEM_TIME ALL p;
GO

PRINT '✅ Created vw_partner_history view';
PRINT '✅ TEMPORAL/AUDIT setup completed successfully';
GO
