-- ===================================================================
-- BỔ SUNG SỐ ĐIỆN THOẠI CHO TÀI XẾ (Driver App login)
-- ===================================================================
-- Triệu chứng
--   Driver App đăng nhập bằng SĐT (POST /api/auth/dev-login { phone }).
--   Nhưng 55/55 user trong tenant.user_accounts đều có phone = NULL
--   => KHÔNG user nào đăng nhập được bằng SĐT.
--   => Acceptance A1 của plan ("đăng nhập được bằng SĐT thật") FAIL.
--
-- Nguyên nhân
--   Dữ liệu import từ CoreData không có cột phone cho user_accounts.
--
-- Cách sửa
--   Gán SĐT cho các user tài xế (role SALES / WAREHOUSE dùng làm tài xế
--   giao hàng) theo pattern 0909xxxxxx, đảm bảo UNIQUE.
--   Giữ các user quản trị (ADMIN/MANAGER) để đăng nhận bằng email.
--
-- Lưu ý
--   - Script idempotent: chạy lại không ghi đè SĐT đã có.
--   - SĐT là dữ liệu GIẢ LẬP cho môi trường dev/UAT. Trước khi lên
--     production phải thay bằng SĐT thật của tài xế.
-- ===================================================================

SET NOCOUNT ON;

-- ── 1. Kiểm tra role "driver" đã dùng ở đâu chưa ─────────────
--   LƯU Ý: tenant.user_accounts.role lưu role dạng CHUỖI, không phải
--   FK tới sys.roles. Vì vậy không cần (và không thể) CREATE ROLE ở đây.
--   Role "driver" được dùng làm nhãn phân quyền ở tầng ứng dụng.
SELECT role, COUNT(*) AS n
FROM tenant.user_accounts
WHERE role = 'driver'
GROUP BY role;

PRINT '[5C.1] Role "driver" là nhãn chuỗi, không cần CREATE ROLE.';
GO

-- ── 2. Gán SĐT cho tài xế (role SALES / WAREHOUSE) ─────────────
--   Pattern: 0909 + 6 chữ số tuần tự, bắt đầu từ 000001.
--   Chỉ gán cho user CHƯA có phone => idempotent.
DECLARE @i INT = 0;
DECLARE @phone VARCHAR(20);
DECLARE @uid UNIQUEIDENTIFIER;

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT id
    FROM tenant.user_accounts
    WHERE (phone IS NULL OR LTRIM(RTRIM(phone)) = '')
      AND role IN ('SALES', 'WAREHOUSE')
    ORDER BY username;

OPEN cur;
FETCH NEXT FROM cur INTO @uid;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @i = @i + 1;
    SET @phone = '0909' + RIGHT('000000' + CAST(@i AS VARCHAR(6)), 6);

    UPDATE tenant.user_accounts
    SET phone = @phone
    WHERE id = @uid;

    FETCH NEXT FROM cur INTO @uid;
END

CLOSE cur;
DEALLOCATE cur;

PRINT '[5C.1] Đã gán SĐT cho ' + CAST(@i AS VARCHAR(10)) + ' user (role SALES/WAREHOUSE).';
GO

-- ── 3. Gán SĐT riêng cho 5 tài xế "chính" (để test app) ─────
--   Dùng email để xác định user -> tránh phụ thuộc thứ tự row.
DECLARE @emails TABLE (email VARCHAR(200), phone VARCHAR(20));

INSERT INTO @emails (email, phone) VALUES
    ('nhanvien_329rm48601@phn.vn',  '0909123456'),
    ('nhanvien_329rm486011@phn.vn', '0909123457'),
    ('nhanvien_329rm48603@phn.vn',  '0909123458'),
    ('nhanvien_329rm48604@phn.vn',  '0909123459'),
    ('nhanvien_329rm48605@phn.vn',  '0909123460');

UPDATE u
SET u.phone = e.phone
FROM tenant.user_accounts u
JOIN @emails e ON LOWER(u.email) = LOWER(e.email);

PRINT '[5C.1] Đã gán SĐT cố định cho 5 tài xế test.';
GO

-- ── 4. Kiểm tra kết quả ─────────────────────────────────────
SET NOCOUNT ON;
SELECT
    role,
    COUNT(*)                                                   AS total_users,
    SUM(CASE WHEN phone IS NOT NULL AND LTRIM(RTRIM(phone)) <> '' THEN 1 ELSE 0 END) AS co_phone
FROM tenant.user_accounts
GROUP BY role
ORDER BY role;

SELECT TOP 10 id, username, email, phone, role
FROM tenant.user_accounts
WHERE phone IS NOT NULL
ORDER BY phone;

-- Kiểm tra trùng SĐT (phải = 0)
SELECT COUNT(*) AS trung_phone
FROM (
    SELECT phone FROM tenant.user_accounts
    WHERE phone IS NOT NULL AND LTRIM(RTRIM(phone)) <> ''
    GROUP BY phone HAVING COUNT(*) > 1
) d;
GO
