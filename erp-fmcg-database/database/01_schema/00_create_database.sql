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
-- NGÀY 1: LEAN SCHEMA - TẠO DATABASE + 12 BẢNG
-- ERP-FMCG Database - MS SQL Server 2022
-- ===================================================================
-- Ngày tạo: 17/09/2026
-- Nguyên tắc: Lean Schema (Zero-Null)
--   - Chỉ giữ  có Fill Rate >= 95% hoặc nghiệp vụ bắt buộc
--   - Phẳng hóa Master Data (khớp 1:1 cấu trúc Excel)
--   - Temporal Tables cho các bảng cần audit
-- ===================================================================

-- 1. Tạo Database
USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'ERP_FMCG')
BEGIN
    CREATE DATABASE ERP_FMCG
    COLLATE Vietnamese_CI_AS;
END
GO

USE ERP_FMCG;
GO

-- 2. Tạo Schemacột
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'tenant')
BEGIN
    EXEC('CREATE SCHEMA tenant');
END
GO

-- 3. Xoá schema 'import' (orphan) - không entity nào map tới
--    EF Core map tất cả vào schema 'tenant':
--      [Table("import_jobs", "tenant")], [Table("import_staging", "tenant")],
--      [Table("import_errors", "tenant")], [Table("import_field_mappings", "tenant")],
--      [Table("audit_logs", "tenant")]
--    Giữ schema 'import' sẽ gây nhầm lẫn khi đóng gói (F7).
IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'import')
BEGIN
    -- Chỉ xoá nếu không còn bảng nào thuộc schema này
    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('import'))
    BEGIN
        EXEC('DROP SCHEMA import');
        PRINT '✅ Đã xoá schema orphan [import]';
    END
    ELSE
        PRINT '⚠️ Schema [import] còn bảng - giữ lại (xem 03_staging_tables.sql bản cũ)';
END
GO

PRINT '✅ Database ERP_FMCG + Schema tenant đã sẵn sàng';
GO
