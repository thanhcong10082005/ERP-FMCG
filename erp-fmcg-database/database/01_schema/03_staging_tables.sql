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
-- STAGING TABLES + IMPORT PIPELINE
-- Chạy script này để tạo các bảng staging cho import workflow
-- ===================================================================

USE ERP_FMCG;
GO

-- ================================================================
-- 1. import_jobs: Theo dõi job import
-- ================================================================
IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'tenant' AND t.name = 'import_jobs')
BEGIN
    CREATE TABLE [tenant].[import_jobs] (
        id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        job_code            NVARCHAR(50) UNIQUE NOT NULL,
        import_type         NVARCHAR(50) NOT NULL,    -- 'CUSTOMERS', 'PRODUCTS', 'ORDERS', 'DCR'
        
        status              NVARCHAR(30) NOT NULL DEFAULT 'PENDING',
        -- PENDING → VALIDATING → VALIDATED → IMPORTING → COMPLETED / COMPLETED_WITH_ERRORS / FAILED
        
        file_name           NVARCHAR(255),
        file_path           NVARCHAR(500),
        file_size           BIGINT DEFAULT 0,
        file_hash           NVARCHAR(64),
        
        total_rows          INT DEFAULT 0,
        processed_rows      INT DEFAULT 0,
        success_rows        INT DEFAULT 0,
        error_rows          INT DEFAULT 0,
        warning_rows        INT DEFAULT 0,
        
        error_handling      NVARCHAR(20) DEFAULT 'SKIP',  -- 'STOP', 'SKIP', 'REVIEW'
        
        uploaded_by         UNIQUEIDENTIFIER,
        uploaded_at         DATETIME2 DEFAULT GETDATE(),
        
        is_committed        BIT DEFAULT 0,
        committed_at       DATETIME2,
        completed_at        DATETIME2,
        
        error_summary       NVARCHAR(1000) NULL,
        
        created_at          DATETIME2 DEFAULT GETDATE(),
        updated_at          DATETIME2 DEFAULT GETDATE(),
        created_by          UNIQUEIDENTIFIER,
        updated_by          UNIQUEIDENTIFIER
    );
    
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_ij_status' AND object_id = OBJECT_ID(N'[tenant].[import_jobs]'))
    CREATE INDEX idx_ij_status ON [tenant].[import_jobs](status);
ELSE
    PRINT 'Bỏ qua index idx_ij_status (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_ij_type' AND object_id = OBJECT_ID(N'[tenant].[import_jobs]'))
    CREATE INDEX idx_ij_type ON [tenant].[import_jobs](import_type);
ELSE
    PRINT 'Bỏ qua index idx_ij_type (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_ij_uploaded_at' AND object_id = OBJECT_ID(N'[tenant].[import_jobs]'))
    CREATE INDEX idx_ij_uploaded_at ON [tenant].[import_jobs](uploaded_at DESC);
ELSE
    PRINT 'Bỏ qua index idx_ij_uploaded_at (đã tồn tại)';
END;
GO

-- ================================================================
-- 2. import_staging: Dữ liệu tạm sau khi parse
-- ================================================================
IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'tenant' AND t.name = 'import_staging')
BEGIN
    CREATE TABLE [tenant].[import_staging] (
        id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        job_id              UNIQUEIDENTIFIER NOT NULL,
        
        row_number          INT NOT NULL,
        row_hash            NVARCHAR(64),         -- Hash của dòng để detect duplicate
        
        raw_data            NVARCHAR(MAX),         -- JSON string gốc
        
        -- Parsed data
        parsed_data         NVARCHAR(MAX),         -- JSON đã parse thành object
        
        -- Validation
        validation_status   NVARCHAR(20) DEFAULT 'PENDING',
        -- PENDING → VALID → WARNING → ERROR
        
        validation_errors   NVARCHAR(MAX),         -- JSON array of errors
        validation_warnings NVARCHAR(MAX),         -- JSON array of warnings
        
        -- Target (sau khi commit)
        target_id           UNIQUEIDENTIFIER,      -- ID đã insert vào bảng chính
        target_table       NVARCHAR(50),
        is_preview         BIT DEFAULT 1,          -- 1 = preview, 0 = committed
        
        -- Row source
        source_file         NVARCHAR(255),
        source_sheet       NVARCHAR(100),
        
        created_at         DATETIME2 DEFAULT GETDATE()
    );
    
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_is_job' AND object_id = OBJECT_ID(N'[tenant].[import_staging]'))
    CREATE INDEX idx_is_job ON [tenant].[import_staging](job_id);
ELSE
    PRINT 'Bỏ qua index idx_is_job (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_is_status' AND object_id = OBJECT_ID(N'[tenant].[import_staging]'))
    CREATE INDEX idx_is_status ON [tenant].[import_staging](validation_status);
ELSE
    PRINT 'Bỏ qua index idx_is_status (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_is_job_status' AND object_id = OBJECT_ID(N'[tenant].[import_staging]'))
    CREATE INDEX idx_is_job_status ON [tenant].[import_staging](job_id, validation_status);
ELSE
    PRINT 'Bỏ qua index idx_is_job_status (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_is_target' AND object_id = OBJECT_ID(N'[tenant].[import_staging]'))
    CREATE INDEX idx_is_target ON [tenant].[import_staging](target_id) WHERE target_id IS NOT NULL;
ELSE
    PRINT 'Bỏ qua index idx_is_target (đã tồn tại)';
END;
GO

-- ================================================================
-- 3. import_errors: Chi tiết lỗi validation
-- ================================================================
IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'tenant' AND t.name = 'import_errors')
BEGIN
    CREATE TABLE [tenant].[import_errors] (
        id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        job_id              UNIQUEIDENTIFIER NOT NULL,
        staging_id          UNIQUEIDENTIFIER,
        
        row_number          INT NOT NULL,
        field_name          NVARCHAR(100),
        field_value         NVARCHAR(MAX),
        
        error_code          NVARCHAR(50) NOT NULL,
        error_message       NVARCHAR(500) NOT NULL,
        
        severity            NVARCHAR(10) DEFAULT 'ERROR',  -- 'WARNING', 'ERROR'
        
        -- Gợi ý cách sửa (EF Core: ImportError.SuggestedFix [MaxLength(500)])
        suggested_fix       NVARCHAR(500) NULL,
        
        -- Resolution
        resolved_value      NVARCHAR(MAX),
        resolved_by         UNIQUEIDENTIFIER,
        resolved_at         DATETIME2,
        is_resolved         BIT DEFAULT 0,
        
        created_at          DATETIME2 DEFAULT GETDATE()
    );
    
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_ie_job' AND object_id = OBJECT_ID(N'[tenant].[import_errors]'))
    CREATE INDEX idx_ie_job ON [tenant].[import_errors](job_id);
ELSE
    PRINT 'Bỏ qua index idx_ie_job (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_ie_staging' AND object_id = OBJECT_ID(N'[tenant].[import_errors]'))
    CREATE INDEX idx_ie_staging ON [tenant].[import_errors](staging_id);
ELSE
    PRINT 'Bỏ qua index idx_ie_staging (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_ie_row' AND object_id = OBJECT_ID(N'[tenant].[import_errors]'))
    CREATE INDEX idx_ie_row ON [tenant].[import_errors](row_number);
ELSE
    PRINT 'Bỏ qua index idx_ie_row (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_ie_resolved' AND object_id = OBJECT_ID(N'[tenant].[import_errors]'))
    CREATE INDEX idx_ie_resolved ON [tenant].[import_errors](is_resolved) WHERE is_resolved = 0;
ELSE
    PRINT 'Bỏ qua index idx_ie_resolved (đã tồn tại)';
END;
GO

-- ================================================================
-- 4. import_field_mappings: Map column Excel → DB field
-- ================================================================
IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'tenant' AND t.name = 'import_field_mappings')
BEGIN
    CREATE TABLE [tenant].[import_field_mappings] (
        id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        
        import_type         NVARCHAR(50) NOT NULL,   -- 'CUSTOMERS', 'PRODUCTS', 'ORDERS'
        
        excel_column        NVARCHAR(100) NOT NULL,   -- Tên cột trong Excel
        db_field           NVARCHAR(100) NOT NULL,   -- Tên cột trong DB
        
        data_type          NVARCHAR(50),            -- 'string', 'int', 'decimal', 'date', 'bool'
        
        is_required        BIT DEFAULT 0,
        default_value      NVARCHAR(255),
        
        validation_rule    NVARCHAR(MAX),           -- JSON validation config
        
        sort_order         INT DEFAULT 0,
        is_active          BIT DEFAULT 1,
        
        created_at         DATETIME2 DEFAULT GETDATE(),
        updated_at         DATETIME2 DEFAULT GETDATE()
    );
    
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_ifm_type_column' AND object_id = OBJECT_ID(N'[tenant].[import_field_mappings]'))
    CREATE UNIQUE INDEX idx_ifm_type_column ON [tenant].[import_field_mappings](import_type, excel_column);
ELSE
    PRINT 'Bỏ qua index idx_ifm_type_column (đã tồn tại)';
END;
GO

PRINT '✅ Staging tables created successfully';
GO
