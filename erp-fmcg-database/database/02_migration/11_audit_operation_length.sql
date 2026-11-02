-- ===================================================================
-- Migration 11 — Mở rộng cột [operation] của bảng tenant.audit_logs
-- -------------------------------------------------------------------
-- FIX — lỗi "String or binary data would be truncated ... column 'operation'".
--
--   SYMPTOM (triệu chứng):
--     Mọi write operation có path chứa GUID dài đều KHÔNG được ghi audit log.
--     Backend log:
--       Microsoft.Data.SqlClient.SqlException (0x80131904):
--       String or binary data would be truncated in table
--       'ERP_FMCG.tenant.audit_logs', column 'operation'.
--       Truncated value: 'PUT /api/sales/trips/f906bc68-126f-4344-8237-f2f5...'
--     ⇒ AuditLogMiddleware bắt exception → request vẫn trả 200 nhưng
--       KHÔNG có dấu vết audit (mất dấu vết gian lận — vấn đề nghiêm trọng).
--     ⇒ Kèm theo transaction bị rollback, gây log nhiễu.
--
--   ROOT CAUSE (nguyên nhân gốc):
--     Cột [operation] được khai báo NVARCHAR(50).
--     AuditLogMiddleware ghi `operation = $"{METHOD} {path}"`.
--     Path có GUID dài hơn 50 ký tự ⇒ vượt giới hạn ⇒ SQL Server 2628 ⇒ mất toàn bộ audit log.
--
--   FIX (cách sửa):
--     1. ALTER COLUMN mở rộng NVARCHAR(50) → NVARCHAR(500).
--     2. Kèm guard ở tầng application (AuditService.LogAsync) cắt chuỗi
--        về đúng độ dài cột để không bao giờ vượt (defense in depth).
--        → xem Pack/erp-fmcg-backend-csharp/.../AuditService.cs
--
--   PREVENTION (ngăn tái diễn):
--     - Tầng service luôn truncate theo giới hạn cột DB.
--     - Script verify 12_verify.sql kiểm tra độ dài cột audit_logs
--       sau khi build schema.
-- ===================================================================

USE ERP_FMCG;
GO

IF COL_LENGTH('tenant.audit_logs', 'operation') IS NULL
BEGIN
    PRINT 'WARN: tenant.audit_logs khong ton tai - bo qua';
END
ELSE
BEGIN
    -- Xem độ dài hiện tại để tránh ALTER thừa.
    DECLARE @currentLen INT = (
        SELECT CHARACTER_MAXIMUM_LENGTH
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = 'tenant'
          AND TABLE_NAME   = 'audit_logs'
          AND COLUMN_NAME = 'operation'
    );

    IF @currentLen < 500
    BEGIN
        ALTER TABLE tenant.audit_logs
            ALTER COLUMN operation NVARCHAR(500) NULL;
        PRINT 'OK: da mo rong tenant.audit_logs.operation NVARCHAR(50) -> NVARCHAR(500)';
    END
    ELSE
    BEGIN
        PRINT 'OK: tenant.audit_logs.operation da du lau (>= 500) - bo qua';
    END
END
GO

-- Ghi chú:
--   Nếu audit_logs đã bị mất dữ liệu do lỗi này, không thể khôi phục
--   (audit log là append-only). Biện pháp phòng ngừa đã áp dụng ở cả
--   tầng DB (độ dài cột) lẫn tầng application (truncate guard).
GO
