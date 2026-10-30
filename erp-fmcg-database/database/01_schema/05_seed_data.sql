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
-- NGÀY 1.5: SEED DATA (nền tảng - dữ liệu thật do pipeline nạp)
-- -------------------------------------------------------------------
-- THAY ĐỔI (F4) so với bản cũ:
--   1. XOÁ lệnh chèn cưỡng ép giá trị ID của bảng categories - cột id là
--      UNIQUEIDENTIFIER, không phải cột tự tăng -> gây Msg 8106 (V3).
--   2. XOÁ khối 10 SKU demo 10000001..10000010:
--      - products_clean.csv chứa SKU 10000003 -> trùng UNIQUE (V5)
--      - SKU demo là dữ liệu giả, gây nhiễu báo cáo
--   3. Thay 5 nhóm PEPSI/SNACK/... bằng 9 nhóm thật từ RDT (N10)
--   4. Thêm 16 tuyến thật từ RDT (có Tên Route)
--   5. Warehouse dùng mã KHO001 (khớp RDT 'Mã Kho' = MAIN_WHS)
--   6. Không seed products/partners - pipeline 01_full_import_pipeline.py
--      nạp từ RDT (141 SKU / 2.723 khách) để tránh trùng lặp
-- ===================================================================

USE ERP_FMCG;
GO

-- ============================================================
-- Seed: Categories (9 nhóm thật từ RDT 'Tên Ngành Hàng')
-- Đã kiểm chứng: CSD, WATER, ENERGY, TEA, SPO, JUICE, JUICY MILK, POSM L2, COFFEE
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM tenant.categories WHERE category_code = 'CSD')
BEGIN
    INSERT INTO tenant.categories (id, category_code, category_name, is_active) VALUES
    (NEWID(), 'CSD',         N'CSD (Canned Soft Drink)', 1),
    (NEWID(), 'WATER',       N'Nước',                    1),
    (NEWID(), 'ENERGY',      N'Năng lượng',               1),
    (NEWID(), 'TEA',         N'Trà',                      1),
    (NEWID(), 'SPO',         N'SPO',                      1),
    (NEWID(), 'JUICE',       N'Nước ép',                  1),
    (NEWID(), 'JUICY_MILK',  N'Sữa tươi',                1),
    (NEWID(), 'POSM_L2',     N'POSM L2',                  1),
    (NEWID(), 'COFFEE',      N'Cà phê',                   1);
END
GO

PRINT '✅ Categories seed: 9 nhóm (từ RDT)';
GO

-- ============================================================
-- Seed: Routes (16 tuyến thật từ RDT 'Mã Route' / 'Tên Route')
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM tenant.routes WHERE route_code = '329RM48601')
BEGIN
    INSERT INTO tenant.routes (route_code, route_name, business_line, region, description) VALUES
    ('329RM48601', N'Trần Minh Trung',              'PEPSI',   N'Long An',    N'Presell'),
    ('329RM48602', N'Nguyễn Thị Bảo Yến',           'PEPSI',   N'Long An',    N'Presell'),
    ('329RM48603', N'Huỳnh kiến thiết',             'PEPSI',   N'Long An',    N'Presell'),
    ('329RM48604', N'Nguyễn Thị Kim Thoa',          'PEPSI',   N'Long An',    N'Presell'),
    ('329RM48605', N'Nguyễn Ngọc Hoàng Thanh',      'PEPSI',   N'Long An',    N'Presell'),
    ('329RM48606', N'Nguyễn Văn Lời',               'PEPSI',   N'Long An',    N'Presell'),
    ('329RM48607', N'Đoàn Hồng Nhung',              'PEPSI',   N'Long An',    N'Presell'),
    ('329RM48608', N'Nguyễn Văn Trí',               'PEPSI',   N'Long An',    N'Presell'),
    ('329RM48609', N'Đỗ Lê Ngọc Sen',               'PEPSI',   N'Long An',    N'Presell'),
    ('329RM48610', N'Lương văn sáng',               'PEPSI',   N'Long An',    N'Presell'),
    ('329RM48612', N'Nguyễn Quốc Bão',              'PEPSI',   N'Long An',    N'Presell'),
    ('329RM48613', N'Nguyễn Minh Trí',              'PEPSI',   N'Long An',    N'Presell'),
    ('329RAH0001', N'AFH099_Huỳnh Phú Hướng',       'GENERAL', N'Tây Ninh',   N'AFH Hunting'),
    ('329RSS0001', N'Route SS Lẻ',                  'GENERAL', N'Tây Ninh',   N'Distributor'),
    ('329RWS0001', N'WSR133_Lê Quốc Tiển',          'GENERAL', N'Tây Ninh',   N'SPVBWholesale'),
    ('329RWS0002', N'WSR147_Nguyễn Thành Nhẫn',     'GENERAL', N'Tây Ninh',   N'SPVBWholesale');
END
GO

PRINT '✅ Routes seed: 16 tuyến (từ RDT)';
GO

-- ============================================================
-- Seed: Warehouses
-- Mã KHO001 = mã thật trong warehouses_clean.csv, tương ứng MAIN_WHS của RDT
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM tenant.warehouses WHERE warehouse_code = 'KHO001')
BEGIN
    INSERT INTO tenant.warehouses (warehouse_code, warehouse_name, warehouse_type, address) VALUES
    ('KHO001', N'Kho trung tâm', 'CENTRAL', N'Kho trung tâm - Long An / Tây Ninh');
END
GO

PRINT '✅ Warehouses seed: KHO001';
GO

-- ============================================================
-- Seed: User Accounts (quản trị hệ thống)
-- 16 NVBH (SP0000xxxx) từ RDT sẽ được pipeline nạp - không seed ở đây
-- để tránh trùng (R15)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM tenant.user_accounts WHERE username = 'admin')
BEGIN
    INSERT INTO tenant.user_accounts (username, full_name, email, role, is_active) VALUES
    ('admin',   N'Quản trị viên',   'admin@erp.vn',    'ADMIN',      1),
    ('SL0001',  N'Lâm Văn Liến',    'nhanvien_sl0001@phn.vn', 'SALES',      1),
    ('SL0002',  N'Nguyễn Thị Nghĩa','nhanvien_sl0002@phn.vn', 'SALES',      1),
    ('SL0003',  N'Văn Vĩnh Phước', 'nhanvien_sl0003@phn.vn', 'SALES',      1),
    ('SL0004',  N'Đào Văn Châu',   'nhanvien_sl0004@phn.vn', 'SALES',      1),
    ('KT002',   N'Kế toán 02',      'kt002@phn.vn',   'ACCOUNTANT', 1),
    ('GH0025',  N'Giám đốc 25',     'gh0025@phn.vn',  'MANAGER',    1);
END
GO

PRINT '✅ User Accounts seed: 7 users hệ thống (16 NVBH do pipeline nạp)';
GO

PRINT '🎉 Ngày 1.5 hoàn tất: Schema + seed nền tảng sẵn sàng';
PRINT '   → Bước tiếp theo: chạy scripts/01_full_import_pipeline.py để nạp dữ liệu thật';
GO
