-- ===================================================================
-- ERP-FMCG - MS SQL Server 2022 - SCHEMA DDL TỔNG HỢP
-- -------------------------------------------------------------------
-- File này được SINH TỰ ĐỘNG bởi scripts/build_schema.py
-- KHÔNG sửa trực tiếp - hãy sửa file nguồn trong database/01_schema/
-- -------------------------------------------------------------------
-- Target : MS SQL Server 2022 (Express) - COLLATE Vietnamese_CI_AS
-- Schema : tenant
-- Encoding: UTF-8 KHÔNG BOM
-- -------------------------------------------------------------------
-- Thứ tự:
--   1. 00_create_database.sql  - tạo DB + schema tenant
--   2. 01_core_tables.sql      - 6 bảng core (2 temporal)
--   3. 02_business_tables.sql  - 6 bảng business (2 temporal)
--   4. 03_staging_tables.sql   - 4 bảng staging (import pipeline)
--   5. 04_triggers.sql         - trigger tồn kho
--   6. 06_temporal_audit.sql   - audit_logs + views
--   7. 07_delivery_tables.sql  - 5 bảng nghiệp vụ giao hàng
--   8. 05_seed_data.sql        - seed nền tảng
-- ===================================================================



-- ===== 00_create_database.sql =====
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


-- ===== 01_core_tables.sql =====
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
-- NGÀY 1: CORE TABLES (6 bảng)
-- 1. categories  - Nhóm sản phẩm
-- 2. products    - 141 SKU + UOM quy đổi
-- 3. routes      - Tuyến phân phối
-- 4. partners    - Khách hàng/Điểm bán (TEMPORAL)
-- 5. warehouses  - Kho
-- 6. user_accounts - Nhân viên + Route + Mảng KD (TEMPORAL)
-- ===================================================================

USE ERP_FMCG;
GO

-- ============================================================
-- BẢNG 1: categories (Nhóm sản phẩm)
-- ============================================================
IF OBJECT_ID('tenant.categories', 'U') IS NULL
CREATE TABLE tenant.categories (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    category_code   VARCHAR(50) NOT NULL UNIQUE,
    category_name   NVARCHAR(255) NOT NULL,
    parent_id       UNIQUEIDENTIFIER NULL,
    is_active       BIT NOT NULL DEFAULT 1,
    created_at      DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at      DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_categories_parent FOREIGN KEY (parent_id)
        REFERENCES tenant.categories(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_categories_code' AND object_id = OBJECT_ID(N'tenant.categories'))
    CREATE INDEX idx_categories_code ON tenant.categories(category_code);
ELSE
    PRINT 'Bỏ qua index idx_categories_code (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_categories_parent' AND object_id = OBJECT_ID(N'tenant.categories'))
    CREATE INDEX idx_categories_parent ON tenant.categories(parent_id);
ELSE
    PRINT 'Bỏ qua index idx_categories_parent (đã tồn tại)';
PRINT '✅ Bảng categories đã tạo';
GO

-- ============================================================
-- BẢNG 2: products (Sản phẩm - 141 SKU)
-- ============================================================
IF OBJECT_ID('tenant.products', 'U') IS NULL
CREATE TABLE tenant.products (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    sku             VARCHAR(50) NOT NULL UNIQUE,
    product_name    NVARCHAR(500) NOT NULL,
    short_name      NVARCHAR(255) NULL,

    category_id     UNIQUEIDENTIFIER NULL,
    brand           NVARCHAR(100) NULL,

    -- Đơn vị tính cơ sở (Base UOM)
    base_uom        VARCHAR(20) NOT NULL DEFAULT 'CAI',
    -- Trọng lượng/Thể tích (cho báo cáo)
    weight_gram     DECIMAL(10,2) NULL,
    volume_ml       DECIMAL(10,2) NULL,

    -- Giá bán lẻ tham chiếu (giá gốc, không thay đổi theo thời gian)
    base_price      DECIMAL(15,2) NOT NULL DEFAULT 0,

    -- Thuế VAT (%)
    vat_rate        DECIMAL(5,2) NOT NULL DEFAULT 8.00,

    --  barcode + ngưỡng tồn kho
    barcode         VARCHAR(50) NULL,
    min_stock       DECIMAL(15,2) NOT NULL DEFAULT 0,
    max_stock       DECIMAL(15,2) NOT NULL DEFAULT 0,
    reorder_point   DECIMAL(15,2) NOT NULL DEFAULT 0,

    is_active       BIT NOT NULL DEFAULT 1,
    created_at      DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at      DATETIME2 NOT NULL DEFAULT GETDATE(),

    -- Temporal Table columns (F10 - bổ sung products vào danh sách audit)
    SysStartTime    DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    SysEndTime      DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime),

    CONSTRAINT FK_products_category FOREIGN KEY (category_id)
        REFERENCES tenant.categories(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_products_sku' AND object_id = OBJECT_ID(N'tenant.products'))
    CREATE INDEX idx_products_sku ON tenant.products(sku);
ELSE
    PRINT 'Bỏ qua index idx_products_sku (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_products_category' AND object_id = OBJECT_ID(N'tenant.products'))
    CREATE INDEX idx_products_category ON tenant.products(category_id);
ELSE
    PRINT 'Bỏ qua index idx_products_category (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_products_active' AND object_id = OBJECT_ID(N'tenant.products'))
    CREATE INDEX idx_products_active ON tenant.products(is_active);
ELSE
    PRINT 'Bỏ qua index idx_products_active (đã tồn tại)';

-- Bật Temporal Table
ALTER TABLE tenant.products
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = tenant.products_history));
PRINT '✅ Bảng products (TEMPORAL) đã tạo';
GO

-- ============================================================
-- BẢNG 3: routes (Tuyến phân phối)
-- ============================================================
IF OBJECT_ID('tenant.routes', 'U') IS NULL
CREATE TABLE tenant.routes (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    route_code      VARCHAR(50) NOT NULL UNIQUE,
    route_name      NVARCHAR(255) NOT NULL,
    -- Mảng kinh doanh: 'PEPSI', 'GENERAL', 'BOTH'
    business_line   VARCHAR(20) NOT NULL DEFAULT 'GENERAL',
    -- Khu vực
    region          NVARCHAR(100) NULL,
    description     NVARCHAR(500) NULL,
    is_active       BIT NOT NULL DEFAULT 1,
    created_at      DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at      DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_routes_code' AND object_id = OBJECT_ID(N'tenant.routes'))
    CREATE INDEX idx_routes_code ON tenant.routes(route_code);
ELSE
    PRINT 'Bỏ qua index idx_routes_code (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_routes_business' AND object_id = OBJECT_ID(N'tenant.routes'))
    CREATE INDEX idx_routes_business ON tenant.routes(business_line);
ELSE
    PRINT 'Bỏ qua index idx_routes_business (đã tồn tại)';
PRINT '✅ Bảng routes đã tạo';
GO

-- ============================================================
-- BẢNG 4: warehouses (Kho)
-- ============================================================
IF OBJECT_ID('tenant.warehouses', 'U') IS NULL
CREATE TABLE tenant.warehouses (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    warehouse_code  VARCHAR(50) NOT NULL UNIQUE,
    warehouse_name  NVARCHAR(255) NOT NULL,
    warehouse_type  VARCHAR(20) NOT NULL DEFAULT 'MAIN', -- 'MAIN', 'TRANSIT', 'RETURN'
    address         NVARCHAR(500) NULL,
    is_active       BIT NOT NULL DEFAULT 1,
    created_at      DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at      DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_warehouses_code' AND object_id = OBJECT_ID(N'tenant.warehouses'))
    CREATE INDEX idx_warehouses_code ON tenant.warehouses(warehouse_code);
ELSE
    PRINT 'Bỏ qua index idx_warehouses_code (đã tồn tại)';
PRINT '✅ Bảng warehouses đã tạo';
GO

-- ============================================================
-- BẢNG 5: partners (Khách hàng/Điểm bán - TEMPORAL)
-- Lưu ý: Temporal Table - lưu vết lịch sử tự động
-- ============================================================
IF OBJECT_ID('tenant.partners', 'U') IS NULL
CREATE TABLE tenant.partners (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    -- Thông tin cơ bản
    partner_code        VARCHAR(50) NOT NULL,
    partner_name        NVARCHAR(255) NOT NULL,
    tax_code            VARCHAR(50) NULL,

    -- Loại đối tác
    partner_type        VARCHAR(20) NOT NULL,           -- 'STORE', 'ASO', 'CORPORATE', 'AGENCY'

    -- Phân loại nghiệp vụ
    segment             VARCHAR(10) NULL,               -- 'A','B','C','D','E'
    outlet_type_code    VARCHAR(50) NULL,               -- Loại hình cửa hàng
    channel_type        VARCHAR(50) NULL,               -- Kênh phân phối
    business_line       VARCHAR(20) NULL DEFAULT 'GENERAL', -- 'PEPSI', 'GENERAL', 'BOTH'

    -- Thông tin liên hệ
    contact_person      NVARCHAR(255) NULL,
    phone               VARCHAR(20) NULL,
    email               VARCHAR(255) NULL,
    address             NVARCHAR(500) NULL,
    ward                NVARCHAR(100) NULL,
    district            NVARCHAR(100) NULL,
    province            NVARCHAR(100) NULL,

    -- Tọa độ GPS
    latitude            DECIMAL(10,7) NULL,
    longitude           DECIMAL(10,7) NULL,

    -- Gán tuyến
    route_id            UNIQUEIDENTIFIER NULL,
    route_code          VARCHAR(50) NULL,               -- Snapshot để truy vấn nhanh
    dcr_code            VARCHAR(50) NULL,               -- Mã DCR từ hệ thống cũ
    sale_person_id      UNIQUEIDENTIFIER NULL,

    -- Công nợ
    credit_limit        DECIMAL(15,2) NOT NULL DEFAULT 0,
    payment_term_days   INT NOT NULL DEFAULT 0,         -- Số ngày được nợ

    -- công nợ cũ + hiện tại
    -- current_debt : công nợ HIỆN TẠI chưa thu (đơn mới + nợ cũ chuyển sang)
    -- old_debt     : công nợ CŨ chưa thu (giảm dần khi tài xế thu được)
    current_debt      DECIMAL(15,2) NOT NULL DEFAULT 0,
    old_debt          DECIMAL(15,2) NOT NULL DEFAULT 0,
    old_debt_date     DATE NULL,

    -- metadata geocoding
    -- LOW/MEDIUM/HIGH = auto-geocoder; MANUAL = admin/dispatcher sửa tay
    geocoding_confidence  VARCHAR(10) NULL
        CONSTRAINT CHK_partners_geocoding_conf CHECK (
            geocoding_confidence IN ('LOW','MEDIUM','HIGH','MANUAL')),
    last_geocoded_at      DATETIME2 NULL,
    geocoded_address      NVARCHAR(500) NULL,

    -- Trạng thái
    is_active           BIT NOT NULL DEFAULT 1,
    notes               NVARCHAR(500) NULL,

    created_at          DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at          DATETIME2 NOT NULL DEFAULT GETDATE(),

    -- Temporal Table columns
    SysStartTime        DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    SysEndTime          DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime),

    CONSTRAINT FK_partners_route FOREIGN KEY (route_id)
        REFERENCES tenant.routes(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_partners_code' AND object_id = OBJECT_ID(N'tenant.partners'))
    CREATE INDEX idx_partners_code ON tenant.partners(partner_code);
ELSE
    PRINT 'Bỏ qua index idx_partners_code (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_partners_type' AND object_id = OBJECT_ID(N'tenant.partners'))
    CREATE INDEX idx_partners_type ON tenant.partners(partner_type);
ELSE
    PRINT 'Bỏ qua index idx_partners_type (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_partners_route' AND object_id = OBJECT_ID(N'tenant.partners'))
    CREATE INDEX idx_partners_route ON tenant.partners(route_id);
ELSE
    PRINT 'Bỏ qua index idx_partners_route (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_partners_segment' AND object_id = OBJECT_ID(N'tenant.partners'))
    CREATE INDEX idx_partners_segment ON tenant.partners(segment);
ELSE
    PRINT 'Bỏ qua index idx_partners_segment (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_partners_active' AND object_id = OBJECT_ID(N'tenant.partners'))
    CREATE INDEX idx_partners_active ON tenant.partners(is_active);
ELSE
    PRINT 'Bỏ qua index idx_partners_active (đã tồn tại)';

-- Bật Temporal Table
ALTER TABLE tenant.partners
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = tenant.partners_history));
PRINT '✅ Bảng partners (TEMPORAL) đã tạo';
GO

-- ============================================================
-- BẢNG 6: user_accounts (Nhân viên - TEMPORAL)
-- Gán trực tiếp: NV + Route + Mảng KD (không qua bảng phụ)
-- ============================================================
IF OBJECT_ID('tenant.user_accounts', 'U') IS NULL
CREATE TABLE tenant.user_accounts (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    username        VARCHAR(50) NOT NULL UNIQUE,
    full_name       NVARCHAR(255) NOT NULL,
    email           VARCHAR(255) NULL,
    phone           VARCHAR(20) NULL,

    -- Phân quyền
    role            VARCHAR(30) NOT NULL DEFAULT 'SALES', -- 'ADMIN','SALES','WAREHOUSE','ACCOUNTANT','MANAGER'
    warehouse_id    UNIQUEIDENTIFIER NULL,

    -- Tuyến + Mảng KD (phẳng hóa)
    route_id        UNIQUEIDENTIFIER NULL,
    route_code      VARCHAR(50) NULL,
    business_line   VARCHAR(20) NULL DEFAULT 'GENERAL',

    is_active       BIT NOT NULL DEFAULT 1,
    last_login_at   DATETIME2 NULL,
    created_at      DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at      DATETIME2 NOT NULL DEFAULT GETDATE(),

    -- Temporal Table columns
    SysStartTime    DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    SysEndTime      DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime),

    CONSTRAINT FK_users_warehouse FOREIGN KEY (warehouse_id)
        REFERENCES tenant.warehouses(id),
    CONSTRAINT FK_users_route FOREIGN KEY (route_id)
        REFERENCES tenant.routes(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_users_username' AND object_id = OBJECT_ID(N'tenant.user_accounts'))
    CREATE INDEX idx_users_username ON tenant.user_accounts(username);
ELSE
    PRINT 'Bỏ qua index idx_users_username (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_users_role' AND object_id = OBJECT_ID(N'tenant.user_accounts'))
    CREATE INDEX idx_users_role ON tenant.user_accounts(role);
ELSE
    PRINT 'Bỏ qua index idx_users_role (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_users_route' AND object_id = OBJECT_ID(N'tenant.user_accounts'))
    CREATE INDEX idx_users_route ON tenant.user_accounts(route_id);
ELSE
    PRINT 'Bỏ qua index idx_users_route (đã tồn tại)';

ALTER TABLE tenant.user_accounts
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = tenant.user_accounts_history));
PRINT '✅ Bảng user_accounts (TEMPORAL) đã tạo';
GO

PRINT '🎉 Ngày 1.1 hoàn tất: 6 bảng CORE đã sẵn sàng';
GO


-- ===== 02_business_tables.sql =====
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
-- NGÀY 1: BUSINESS TABLES (5 bảng)
-- 1. price_lists        - Bảng giá có hiệu lực theo thời gian
-- 2. sales_orders       - Đơn hàng (TEMPORAL)
-- 3. sales_order_items  - Chi tiết đơn
-- 4. stock_ledger       - Sổ cái kho (Append-only, Columnstore)
-- 5. inventory_balances - Tồn kho realtime (Trigger update)
-- ===================================================================

USE ERP_FMCG;
GO

-- ============================================================
-- BẢNG 7: price_lists (Bảng giá có hiệu lực theo thời gian)
-- ============================================================
IF OBJECT_ID('tenant.price_lists', 'U') IS NULL
CREATE TABLE tenant.price_lists (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    price_list_code VARCHAR(50) NOT NULL,
    price_list_name NVARCHAR(255) NOT NULL,

    -- Áp dụng cho
    product_id      UNIQUEIDENTIFIER NOT NULL,
    partner_id      UNIQUEIDENTIFIER NULL,              -- NULL = áp dụng chung
    segment         VARCHAR(10) NULL,                  -- NULL = áp dụng chung

    -- Giá
    uom_code        VARCHAR(20) NOT NULL DEFAULT 'CAI',
    unit_price      DECIMAL(15,2) NOT NULL,
    min_qty         DECIMAL(15,2) NOT NULL DEFAULT 1,   -- Số lượng tối thiểu để áp dụng giá

    -- Hiệu lực
    effective_date  DATE NOT NULL,
    expiry_date     DATE NULL,

    -- Ưu tiên (cao hơn = áp dụng trước)
    priority        INT NOT NULL DEFAULT 0,

    is_active       BIT NOT NULL DEFAULT 1,
    created_at      DATETIME2 NOT NULL DEFAULT GETDATE(),

    -- Temporal Table columns (F10 - bổ sung price_lists vào danh sách audit)
    SysStartTime    DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    SysEndTime      DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime),

    CONSTRAINT FK_pricelists_product FOREIGN KEY (product_id)
        REFERENCES tenant.products(id),
    CONSTRAINT FK_pricelists_partner FOREIGN KEY (partner_id)
        REFERENCES tenant.partners(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_pricelists_product' AND object_id = OBJECT_ID(N'tenant.price_lists'))
    CREATE INDEX idx_pricelists_product ON tenant.price_lists(product_id);
ELSE
    PRINT 'Bỏ qua index idx_pricelists_product (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_pricelists_partner' AND object_id = OBJECT_ID(N'tenant.price_lists'))
    CREATE INDEX idx_pricelists_partner ON tenant.price_lists(partner_id);
ELSE
    PRINT 'Bỏ qua index idx_pricelists_partner (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_pricelists_effective' AND object_id = OBJECT_ID(N'tenant.price_lists'))
    CREATE INDEX idx_pricelists_effective ON tenant.price_lists(effective_date, expiry_date);
ELSE
    PRINT 'Bỏ qua index idx_pricelists_effective (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_pricelists_active' AND object_id = OBJECT_ID(N'tenant.price_lists'))
    CREATE INDEX idx_pricelists_active ON tenant.price_lists(is_active);
ELSE
    PRINT 'Bỏ qua index idx_pricelists_active (đã tồn tại)';

-- Bật Temporal Table
ALTER TABLE tenant.price_lists
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = tenant.price_lists_history));
PRINT '✅ Bảng price_lists (TEMPORAL) đã tạo';
GO

-- ============================================================
-- BẢNG 8: sales_orders (Đơn hàng - TEMPORAL)
-- ===================================================================
IF OBJECT_ID('tenant.sales_orders', 'U') IS NULL
CREATE TABLE tenant.sales_orders (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    -- Mã đơn (unique theo hệ thống)
    order_number        VARCHAR(50) NOT NULL,
    external_ref        VARCHAR(100) NULL,               -- Mã RD từ Excel nếu có

    -- Quan hệ
    customer_id         UNIQUEIDENTIFIER NOT NULL,
    warehouse_id        UNIQUEIDENTIFIER NOT NULL,
    sale_person_id      UNIQUEIDENTIFIER NULL,

    -- Ngày tháng
    order_date          DATE NOT NULL,
    delivery_date       DATE NULL,
    expected_delivery   DATE NULL,

    -- Địa chỉ giao (snapshot từ partners)
    delivery_address    NVARCHAR(500) NULL,
    contact_phone       VARCHAR(20) NULL,

    -- Giá trị (tính toán từ items)
    subtotal            DECIMAL(15,2) NOT NULL DEFAULT 0,
    discount_amount     DECIMAL(15,2) NOT NULL DEFAULT 0,
    tax_amount          DECIMAL(15,2) NOT NULL DEFAULT 0,
    total_amount        DECIMAL(15,2) NOT NULL DEFAULT 0,

    -- Khuyến mãi trực tiếp (on-the-fly)
    promotion_code      VARCHAR(50) NULL,
    promotion_note      NVARCHAR(500) NULL,

    -- Trạng thái đơn
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    -- 'DRAFT','CONFIRMED','PICKING','SHIPPED','DELIVERED','INVOICED','CANCELLED','RETURNED'

    -- Thanh toán
    payment_status      VARCHAR(20) NOT NULL DEFAULT 'UNPAID',
    -- 'UNPAID','PARTIAL','PAID','OVERDUE'
    paid_amount         DECIMAL(15,2) NOT NULL DEFAULT 0,

    -- snapshot công nợ cũ lúc tạo đơn
    -- Driver App hiển thị "Công nợ cũ" cần thu cho khách này
    old_debt_amount     DECIMAL(15,2) NOT NULL DEFAULT 0,

    -- retry logic (chuyển từ Postgres v13_1_3)
    -- failure_count >= 3 -> tự động CANCELLED
    failure_count       INT NOT NULL DEFAULT 0,
    failure_reason      VARCHAR(50) NULL
        CONSTRAINT CHK_sales_orders_failure_reason CHECK (
            failure_reason IN ('DOOR_LOCKED','WRONG_ORDER','CUSTOMER_ABSENT','TRAFFIC','OTHER')),

    -- Xuất hóa đơn
    invoice_number      VARCHAR(50) NULL,
    invoice_date        DATE NULL,

    notes               NVARCHAR(500) NULL,

    -- Audit
    created_by          UNIQUEIDENTIFIER NULL,
    created_at          DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at          DATETIME2 NOT NULL DEFAULT GETDATE(),

    -- Temporal Table columns
    SysStartTime        DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    SysEndTime          DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime),

    CONSTRAINT FK_orders_customer FOREIGN KEY (customer_id)
        REFERENCES tenant.partners(id),
    CONSTRAINT FK_orders_warehouse FOREIGN KEY (warehouse_id)
        REFERENCES tenant.warehouses(id),
    CONSTRAINT FK_orders_saleperson FOREIGN KEY (sale_person_id)
        REFERENCES tenant.user_accounts(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_orders_number' AND object_id = OBJECT_ID(N'tenant.sales_orders'))
    CREATE UNIQUE INDEX idx_orders_number ON tenant.sales_orders(order_number);
ELSE
    PRINT 'Bỏ qua index idx_orders_number (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_orders_customer' AND object_id = OBJECT_ID(N'tenant.sales_orders'))
    CREATE INDEX idx_orders_customer ON tenant.sales_orders(customer_id);
ELSE
    PRINT 'Bỏ qua index idx_orders_customer (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_orders_warehouse' AND object_id = OBJECT_ID(N'tenant.sales_orders'))
    CREATE INDEX idx_orders_warehouse ON tenant.sales_orders(warehouse_id);
ELSE
    PRINT 'Bỏ qua index idx_orders_warehouse (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_orders_date' AND object_id = OBJECT_ID(N'tenant.sales_orders'))
    CREATE INDEX idx_orders_date ON tenant.sales_orders(order_date);
ELSE
    PRINT 'Bỏ qua index idx_orders_date (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_orders_status' AND object_id = OBJECT_ID(N'tenant.sales_orders'))
    CREATE INDEX idx_orders_status ON tenant.sales_orders(status);
ELSE
    PRINT 'Bỏ qua index idx_orders_status (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_orders_payment' AND object_id = OBJECT_ID(N'tenant.sales_orders'))
    CREATE INDEX idx_orders_payment ON tenant.sales_orders(payment_status);
ELSE
    PRINT 'Bỏ qua index idx_orders_payment (đã tồn tại)';

ALTER TABLE tenant.sales_orders
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = tenant.sales_orders_history));
PRINT '✅ Bảng sales_orders (TEMPORAL) đã tạo';
GO

-- ============================================================
-- BẢNG 9: sales_order_items (Chi tiết đơn hàng)
-- Multi-UOM: Đặt Thùng nhưng quy đổi ra Lon/Cai
-- ===================================================================
IF OBJECT_ID('tenant.sales_order_items', 'U') IS NULL
CREATE TABLE tenant.sales_order_items (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    sales_order_id      UNIQUEIDENTIFIER NOT NULL,
    line_number         INT NOT NULL,

    product_id          UNIQUEIDENTIFIER NOT NULL,
    product_sku         VARCHAR(50) NOT NULL,            -- Snapshot

    -- Đơn vị tính
    uom_code            VARCHAR(20) NOT NULL,            -- ĐVT người dùng đặt (CAI/LON/THUNG)
    ordered_qty         DECIMAL(15,2) NOT NULL,          -- Số lượng theo UOM đặt
    base_qty            DECIMAL(15,2) NOT NULL,          -- Quy đổi về base UOM

    -- Giá
    unit_price          DECIMAL(15,2) NOT NULL,          -- Giá theo UOM đặt
    discount_percent    DECIMAL(5,2) NOT NULL DEFAULT 0,
    discount_amount     DECIMAL(15,2) NOT NULL DEFAULT 0,

    -- Thành tiền
    line_subtotal       DECIMAL(15,2) NOT NULL,          -- ordered_qty * unit_price
    line_total          DECIMAL(15,2) NOT NULL,          -- subtotal - discount

    -- Giao hàng
    delivered_qty       DECIMAL(15,2) NOT NULL DEFAULT 0,
    returned_qty        DECIMAL(15,2) NOT NULL DEFAULT 0,

    -- Lý do (nếu có)
    notes               NVARCHAR(255) NULL,

    created_at          DATETIME2 NOT NULL DEFAULT GETDATE(),

    CONSTRAINT FK_orderitems_order FOREIGN KEY (sales_order_id)
        REFERENCES tenant.sales_orders(id) ON DELETE CASCADE,
    CONSTRAINT FK_orderitems_product FOREIGN KEY (product_id)
        REFERENCES tenant.products(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_orderitems_order' AND object_id = OBJECT_ID(N'tenant.sales_order_items'))
    CREATE INDEX idx_orderitems_order ON tenant.sales_order_items(sales_order_id);
ELSE
    PRINT 'Bỏ qua index idx_orderitems_order (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_orderitems_product' AND object_id = OBJECT_ID(N'tenant.sales_order_items'))
    CREATE INDEX idx_orderitems_product ON tenant.sales_order_items(product_id);
ELSE
    PRINT 'Bỏ qua index idx_orderitems_product (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_orderitems_sku' AND object_id = OBJECT_ID(N'tenant.sales_order_items'))
    CREATE INDEX idx_orderitems_sku ON tenant.sales_order_items(product_sku);
ELSE
    PRINT 'Bỏ qua index idx_orderitems_sku (đã tồn tại)';
PRINT '✅ Bảng sales_order_items đã tạo';
GO

-- ============================================================
-- BẢNG 10: stock_ledger (Sổ cái kho - Append-only, có Columnstore)
-- Append-only = chỉ INSERT, không UPDATE/DELETE
-- Columnstore Index = truy vấn báo cáo cực nhanh
-- ===================================================================
IF OBJECT_ID('tenant.stock_ledger', 'U') IS NULL
CREATE TABLE tenant.stock_ledger (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    -- Thời gian
    transaction_date    DATE NOT NULL,
    transaction_time    DATETIME2 NOT NULL DEFAULT GETDATE(),

    -- Loại giao dịch
    transaction_type    VARCHAR(20) NOT NULL,
    -- 'PURCHASE' (Nhập mua), 'SALE' (Bán), 'TRANSFER_IN', 'TRANSFER_OUT',
    -- 'RETURN_IN' (Khách trả), 'RETURN_OUT' (Trả NCC), 'ADJUSTMENT' (Kiểm kê), 'OPENING' (Tồn đầu kỳ)
    -- chuan hoa CHECK constraint de chan gia tri sai
    --   (truoc do chi co CHK_stockledger_qty, khong kiem tra transaction_type)
    CONSTRAINT CHK_stockledger_type CHECK (
        transaction_type IN ('PURCHASE','SALE','TRANSFER_IN','TRANSFER_OUT',
                             'RETURN_IN','RETURN_OUT','ADJUSTMENT','OPENING')
    ),

    -- Kho & Sản phẩm
    warehouse_id        UNIQUEIDENTIFIER NOT NULL,
    product_id          UNIQUEIDENTIFIER NOT NULL,
    uom_code            VARCHAR(20) NOT NULL DEFAULT 'CAI',

    -- Số lượng (luôn dương với IN, âm với OUT)
    quantity_in         DECIMAL(15,2) NOT NULL DEFAULT 0,
    quantity_out        DECIMAL(15,2) NOT NULL DEFAULT 0,

    -- Giá vốn (cho tính giá xuất kho bình quân)
    unit_cost           DECIMAL(15,2) NULL,

    -- Tham chiếu
    reference_type      VARCHAR(50) NULL,                -- 'SALES_ORDER', 'PURCHASE_ORDER', 'TRANSFER', 'ADJUSTMENT'
    reference_id        UNIQUEIDENTIFIER NULL,

    -- Đối tác
    partner_id          UNIQUEIDENTIFIER NULL,

    notes               NVARCHAR(500) NULL,

    created_at          DATETIME2 NOT NULL DEFAULT GETDATE(),
    created_by          UNIQUEIDENTIFIER NULL,

    CONSTRAINT FK_stockledger_warehouse FOREIGN KEY (warehouse_id)
        REFERENCES tenant.warehouses(id),
    CONSTRAINT FK_stockledger_product FOREIGN KEY (product_id)
        REFERENCES tenant.products(id),
    CONSTRAINT FK_stockledger_partner FOREIGN KEY (partner_id)
        REFERENCES tenant.partners(id),

    CONSTRAINT CHK_stockledger_qty CHECK (
        (quantity_in >= 0 AND quantity_out = 0) OR
        (quantity_in = 0 AND quantity_out >= 0)
    )
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_stockledger_date' AND object_id = OBJECT_ID(N'tenant.stock_ledger'))
    CREATE INDEX idx_stockledger_date ON tenant.stock_ledger(transaction_date);
ELSE
    PRINT 'Bỏ qua index idx_stockledger_date (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_stockledger_warehouse' AND object_id = OBJECT_ID(N'tenant.stock_ledger'))
    CREATE INDEX idx_stockledger_warehouse ON tenant.stock_ledger(warehouse_id);
ELSE
    PRINT 'Bỏ qua index idx_stockledger_warehouse (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_stockledger_product' AND object_id = OBJECT_ID(N'tenant.stock_ledger'))
    CREATE INDEX idx_stockledger_product ON tenant.stock_ledger(product_id);
ELSE
    PRINT 'Bỏ qua index idx_stockledger_product (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_stockledger_type' AND object_id = OBJECT_ID(N'tenant.stock_ledger'))
    CREATE INDEX idx_stockledger_type ON tenant.stock_ledger(transaction_type);
ELSE
    PRINT 'Bỏ qua index idx_stockledger_type (đã tồn tại)';

-- Columnstore Index cho báo cáo (Xuất-Nhập-Tồn cực nhanh)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_stockledger_cs' AND object_id = OBJECT_ID(N'tenant.stock_ledger'))
    CREATE NONCLUSTERED COLUMNSTORE INDEX idx_stockledger_cs
    ON tenant.stock_ledger(transaction_date, warehouse_id, product_id, transaction_type, quantity_in, quantity_out);
ELSE
    PRINT 'Bỏ qua index idx_stockledger_cs (đã tồn tại)';
PRINT '✅ Bảng stock_ledger (Columnstore) đã tạo';
GO

-- ============================================================
-- BẢNG 11: inventory_balances (Tồn kho realtime)
-- Tự động cập nhật qua Trigger khi insert stock_ledger
-- ===================================================================
IF OBJECT_ID('tenant.inventory_balances', 'U') IS NULL
CREATE TABLE tenant.inventory_balances (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    warehouse_id        UNIQUEIDENTIFIER NOT NULL,
    product_id          UNIQUEIDENTIFIER NOT NULL,
    uom_code            VARCHAR(20) NOT NULL DEFAULT 'CAI',

    -- Số lượng
    quantity_on_hand    DECIMAL(15,2) NOT NULL DEFAULT 0,    -- Tồn thực tế
    quantity_reserved   DECIMAL(15,2) NOT NULL DEFAULT 0,    -- Đã giữ chờ (đơn chưa giao)
    quantity_available  AS (quantity_on_hand - quantity_reserved) PERSISTED,  -- Có thể bán

    -- Giá trị
    average_cost        DECIMAL(15,2) NOT NULL DEFAULT 0,    -- Giá vốn bình quân
    total_value         AS (quantity_on_hand * average_cost) PERSISTED,

    last_transaction_at DATETIME2 NULL,
    updated_at          DATETIME2 NOT NULL DEFAULT GETDATE(),

    CONSTRAINT UK_inventory_balance UNIQUE (warehouse_id, product_id, uom_code),
    CONSTRAINT FK_inv_warehouse FOREIGN KEY (warehouse_id)
        REFERENCES tenant.warehouses(id),
    CONSTRAINT FK_inv_product FOREIGN KEY (product_id)
        REFERENCES tenant.products(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_inv_warehouse' AND object_id = OBJECT_ID(N'tenant.inventory_balances'))
    CREATE INDEX idx_inv_warehouse ON tenant.inventory_balances(warehouse_id);
ELSE
    PRINT 'Bỏ qua index idx_inv_warehouse (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_inv_product' AND object_id = OBJECT_ID(N'tenant.inventory_balances'))
    CREATE INDEX idx_inv_product ON tenant.inventory_balances(product_id);
ELSE
    PRINT 'Bỏ qua index idx_inv_product (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_inv_available' AND object_id = OBJECT_ID(N'tenant.inventory_balances'))
    CREATE INDEX idx_inv_available ON tenant.inventory_balances(quantity_available);
ELSE
    PRINT 'Bỏ qua index idx_inv_available (đã tồn tại)';
PRINT '✅ Bảng inventory_balances đã tạo';
GO

-- ============================================================
-- BẢNG 12: uom_conversions (Bảng quy đổi đơn vị tính)
-- Ví dụ: 1 THUNG = 24 LON = 288 CAI
-- ===================================================================
IF OBJECT_ID('tenant.uom_conversions', 'U') IS NULL
CREATE TABLE tenant.uom_conversions (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    product_id      UNIQUEIDENTIFIER NOT NULL,

    from_uom        VARCHAR(20) NOT NULL,        -- VD: 'THUNG'
    to_uom          VARCHAR(20) NOT NULL,        -- VD: 'LON'
    conversion_factor DECIMAL(15,4) NOT NULL,    -- VD: 24.0000

    is_default      BIT NOT NULL DEFAULT 0,

    created_at      DATETIME2 NOT NULL DEFAULT GETDATE(),

    CONSTRAINT FK_uom_product FOREIGN KEY (product_id)
        REFERENCES tenant.products(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_uom_product' AND object_id = OBJECT_ID(N'tenant.uom_conversions'))
    CREATE INDEX idx_uom_product ON tenant.uom_conversions(product_id);
ELSE
    PRINT 'Bỏ qua index idx_uom_product (đã tồn tại)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_uom_from' AND object_id = OBJECT_ID(N'tenant.uom_conversions'))
    CREATE INDEX idx_uom_from ON tenant.uom_conversions(from_uom);
ELSE
    PRINT 'Bỏ qua index idx_uom_from (đã tồn tại)';
PRINT '✅ Bảng uom_conversions đã tạo';
GO

PRINT '🎉 Ngày 1.2 hoàn tất: 5 bảng BUSINESS + 1 bảng UTILITY đã sẵn sàng';
GO


-- ===== 03_staging_tables.sql =====
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


-- ===== 04_triggers.sql =====
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
-- NGÀY 1: TRIGGER TỰ ĐỘNG CẬP NHẬT TỒN KHO
-- Khi INSERT vào stock_ledger → tự cập nhật inventory_balances
-- ===================================================================

USE ERP_FMCG;
GO

-- ============================================================
-- Trigger: Sau khi INSERT stock_ledger → UPSERT inventory_balances
-- ============================================================
CREATE OR ALTER TRIGGER tenant.trg_stockledger_after_insert
ON tenant.stock_ledger
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    -- UPSERT inventory_balances cho mỗi dòng inserted
    MERGE tenant.inventory_balances AS target
    USING (
        SELECT
            warehouse_id,
            product_id,
            uom_code,
            SUM(quantity_in - quantity_out) AS net_quantity,
            AVG(unit_cost) AS avg_cost,
            MAX(transaction_time) AS last_tx
        FROM inserted
        GROUP BY warehouse_id, product_id, uom_code
    ) AS source
    ON target.warehouse_id = source.warehouse_id
       AND target.product_id = source.product_id
       AND target.uom_code = source.uom_code

    WHEN MATCHED THEN
        UPDATE SET
            quantity_on_hand = target.quantity_on_hand + source.net_quantity,
            -- NULLIF(...,0) gây NULL cho average_cost (NOT NULL)
            --   khi tồn về đúng 0 → lỗi 515. Chỉ chia khi tồn còn > 0.
            average_cost = CASE
                WHEN target.quantity_on_hand + source.net_quantity > 0
                 AND source.avg_cost IS NOT NULL AND source.avg_cost > 0
                THEN (target.quantity_on_hand * target.average_cost + source.net_quantity * source.avg_cost)
                     / (target.quantity_on_hand + source.net_quantity)
                ELSE target.average_cost
            END,
            last_transaction_at = source.last_tx,
            updated_at = GETDATE()

    WHEN NOT MATCHED THEN
        INSERT (warehouse_id, product_id, uom_code, quantity_on_hand, average_cost, last_transaction_at)
        VALUES (source.warehouse_id, source.product_id, source.uom_code,
                source.net_quantity, ISNULL(source.avg_cost, 0), source.last_tx);
END
GO

PRINT '✅ Trigger trg_stockledger_after_insert đã tạo';
PRINT '   → INSERT stock_ledger sẽ tự động cập nhật inventory_balances';
GO

PRINT '🎉 Ngày 1.4 hoàn tất: Trigger tự động cập nhật tồn kho';
GO


-- ===== 06_temporal_audit.sql =====
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


-- ===== 07_delivery_tables.sql =====
-- ===================================================================
-- ANSI SETTINGS - BAT BUOC (F2)
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
-- NGHIEP VU GIAO HANG (5 bang)
-- Chuyen tu NestJS/Postgres (ERP_FMCG_v6.sql + migration v13_3, v10_4)
-- sang MS SQL Server 2022.
--
-- Bang: delivery_trips, delivery_trip_orders, delivery_pod, pod_items,
--       pod_attachments
--
-- QUYET DINH KIEN TRUC:
--   D1 - Giu UNIQUEIDENTIFIER (Guid). Driver App doi `number` -> `string`.
--   D5 - client_pod_id UNIQUE = idempotency key (chong double-POD).
--         Da xac minh double-POD xay ra THAT tren production Supabase
--          (6 nhom POD trung trong Supabase_export/delivery_pod_rows.csv).
--   D6 - Media luu file_path (khong luu base64 trong DB).
--   D7 - Ghi stock_ledger khi POD xac nhan giao hang.
-- ===================================================================

USE ERP_FMCG;
GO

-- ============================================================
-- BANG 1: delivery_trips (Chuyen giao - TEMPORAL)
-- ============================================================
IF OBJECT_ID('tenant.delivery_trips', 'U') IS NULL
CREATE TABLE tenant.delivery_trips (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    trip_number         VARCHAR(30) NOT NULL,             -- TRIP-20261029-000001
    trip_date           DATE NOT NULL,

    warehouse_id        UNIQUEIDENTIFIER NOT NULL,
    driver_id           UNIQUEIDENTIFIER NULL,
    vehicle_plate       VARCHAR(20) NULL,

    -- Trang thai chuyen
    status              VARCHAR(20) NOT NULL DEFAULT 'PREPARING',
    -- 'PREPARING','LOADING','DELIVERING','COMPLETED','PARTIAL_DELIVERED','CANCELLED'
    -- ★ BAT BUOC co 'PARTIAL_DELIVERED': completeTrip() cu cua Driver App
    --   set gia tri nay nhung schema Postgres CU THIEU -> insert bi reject.

    -- Tong quan
    total_orders        INT NOT NULL DEFAULT 0,
    total_amount        DECIMAL(15,2) NOT NULL DEFAULT 0,
    delivered_count     INT NOT NULL DEFAULT 0,
    failed_count        INT NOT NULL DEFAULT 0,

    -- Tien thu
    total_cash          DECIMAL(15,2) NOT NULL DEFAULT 0,
    total_transfer      DECIMAL(15,2) NOT NULL DEFAULT 0,
    total_old_debt      DECIMAL(15,2) NOT NULL DEFAULT 0,
    -- ★ DB-C7: NestJS getTripPartnerOldDebts() tra total_current_debt cho
    --   dispatcher. Migration Postgres v13_3__trip_old_debts.sql da co,
    --   MS SQL chua co -> thieu.
    total_current_debt  DECIMAL(15,2) NOT NULL DEFAULT 0,

    -- Thoi gian
    started_at          DATETIME2 NULL,
    completed_at        DATETIME2 NULL,

    notes               NVARCHAR(500) NULL,
    created_by          UNIQUEIDENTIFIER NULL,
    created_at          DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at          DATETIME2 NOT NULL DEFAULT GETDATE(),

    -- Temporal Table columns
    SysStartTime        DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    SysEndTime          DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime),

    CONSTRAINT UQ_delivery_trips_number UNIQUE (trip_number),
    CONSTRAINT FK_delivery_trips_warehouse FOREIGN KEY (warehouse_id)
        REFERENCES tenant.warehouses(id),
    CONSTRAINT FK_delivery_trips_driver FOREIGN KEY (driver_id)
        REFERENCES tenant.user_accounts(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_delivery_trips_driver_status' AND object_id = OBJECT_ID(N'tenant.delivery_trips'))
    CREATE INDEX idx_delivery_trips_driver_status ON tenant.delivery_trips(driver_id, status);
ELSE
    PRINT 'Bo qua index idx_delivery_trips_driver_status (da ton tai)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_delivery_trips_date' AND object_id = OBJECT_ID(N'tenant.delivery_trips'))
    CREATE INDEX idx_delivery_trips_date ON tenant.delivery_trips(trip_date DESC);
ELSE
    PRINT 'Bo qua index idx_delivery_trips_date (da ton tai)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_delivery_trips_status' AND object_id = OBJECT_ID(N'tenant.delivery_trips'))
    CREATE INDEX idx_delivery_trips_status ON tenant.delivery_trips(status);
ELSE
    PRINT 'Bo qua index idx_delivery_trips_status (da ton tai)';

ALTER TABLE tenant.delivery_trips
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = tenant.delivery_trips_history));
PRINT 'OK: Bang delivery_trips (TEMPORAL) da tao';
GO

-- ============================================================
-- BANG 2: delivery_trip_orders (Don hang trong chuyen)
-- ============================================================
IF OBJECT_ID('tenant.delivery_trip_orders', 'U') IS NULL
CREATE TABLE tenant.delivery_trip_orders (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    trip_id             UNIQUEIDENTIFIER NOT NULL,
    sales_order_id      UNIQUEIDENTIFIER NOT NULL,
    stop_order          INT NOT NULL DEFAULT 1,

    status              VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    -- 'PENDING','DELIVERED','PARTIAL','FAILED','CANCELLED'

    -- ★ DB-C11: Driver App POST failure_reason len POD voi 5 gia tri nay
    failure_reason      NVARCHAR(255) NULL,
    failure_count       INT NOT NULL DEFAULT 0,

    -- Snapshot cong no cu luc gan chuyen (D8)
    old_debt_amount     DECIMAL(15,2) NOT NULL DEFAULT 0,

    notes               NVARCHAR(500) NULL,
    created_at          DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at          DATETIME2 NOT NULL DEFAULT GETDATE(),

    CONSTRAINT UQ_delivery_trip_orders UNIQUE (trip_id, sales_order_id),
    CONSTRAINT FK_delivery_trip_orders_trip FOREIGN KEY (trip_id)
        REFERENCES tenant.delivery_trips(id) ON DELETE CASCADE,
    CONSTRAINT FK_delivery_trip_orders_order FOREIGN KEY (sales_order_id)
        REFERENCES tenant.sales_orders(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_dto_trip' AND object_id = OBJECT_ID(N'tenant.delivery_trip_orders'))
    CREATE INDEX idx_dto_trip ON tenant.delivery_trip_orders(trip_id);
ELSE
    PRINT 'Bo qua index idx_dto_trip (da ton tai)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_dto_order' AND object_id = OBJECT_ID(N'tenant.delivery_trip_orders'))
    CREATE INDEX idx_dto_order ON tenant.delivery_trip_orders(sales_order_id);
ELSE
    PRINT 'Bo qua index idx_dto_order (da ton tai)';
PRINT 'OK: Bang delivery_trip_orders da tao';
GO

-- ============================================================
-- BANG 3: delivery_pod (Phieu giao hang - PROOF OF DELIVERY)
-- ★ BAT BUOC client_pod_id UNIQUE (quyet dinh D5) - idempotency
-- ============================================================
IF OBJECT_ID('tenant.delivery_pod', 'U') IS NULL
CREATE TABLE tenant.delivery_pod (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    -- ★ D5: Idempotency key sinh boi CLIENT (UUID v4).
    --   Client tao 1 lan khi bam "Giao hang", gui di may nhieu lan
    --   (retry/offline queue) -> server chi tao 1 POD duy nhat.
    client_pod_id       UNIQUEIDENTIFIER NOT NULL,

    trip_order_id       UNIQUEIDENTIFIER NOT NULL,
    sales_order_id      UNIQUEIDENTIFIER NOT NULL,
    partner_id          UNIQUEIDENTIFIER NOT NULL,

    -- Thoi gian giao
    arrived_at          DATETIME2 NULL,
    delivered_at        DATETIME2 NULL,

    -- Trang thai giao
    delivery_status     VARCHAR(20) NOT NULL DEFAULT 'DELIVERED',
    -- 'DELIVERED','PARTIAL','FAILED'

    -- Ma tran thanh toan 6 o
    -- Nhom "don moi": tien mat / chuyen khoan / ghi no don moi
    new_order_cash      DECIMAL(15,2) NOT NULL DEFAULT 0,
    new_order_transfer  DECIMAL(15,2) NOT NULL DEFAULT 0,
    new_order_credit    DECIMAL(15,2) NOT NULL DEFAULT 0,
    -- Nhom "cong no cu": tien mat / chuyen khoan / ghi no cong no cu
    old_debt_cash       DECIMAL(15,2) NOT NULL DEFAULT 0,
    old_debt_transfer   DECIMAL(15,2) NOT NULL DEFAULT 0,
    old_debt_credit     DECIMAL(15,2) NOT NULL DEFAULT 0,

    -- Tong tien thu
    total_collected     DECIMAL(15,2) NOT NULL DEFAULT 0,

    -- Chu ky / khach xac nhan
    recipient_name      NVARCHAR(255) NULL,
    -- ★ D6: chi luu duong dan file, KHONG luu base64 (base64 2-5MB lam
    --   ngan DB va buffer RAM)
    signature_path      NVARCHAR(500) NULL,

    notes               NVARCHAR(500) NULL,

    -- ★ DB-C4: Driver App da gui failure_reason len POD
    failure_reason      NVARCHAR(255) NULL,

    -- GPS kiem toan (chuyen tu Postgres v10_4__delivery_pod_gps.sql)
    delivery_latitude       DECIMAL(10,7) NULL,
    delivery_longitude      DECIMAL(10,7) NULL,
    delivery_gps_accuracy_m DECIMAL(10,2) NULL,
    distance_to_partner_m   INT NULL,
    gps_captured_at         DATETIME2 NULL,

    -- Ket qua kiem tra chong gian lan
    -- ★ VARCHAR(30): gia tri dai nhat la 'SKIPPED_NO_PARTNER_GPS' (23 ky tu).
    --   VARCHAR(20) se bi loi "String or binary data would be truncated".
    gps_gate_result         VARCHAR(30) NULL,   -- 'PASS','FAIL','SKIPPED_NO_PARTNER_GPS'
    gps_gate_distance_m     INT NULL,
    time_guard_result       VARCHAR(20) NULL,   -- 'PASS','FAIL','SKIPPED_NO_GPS'

    created_by          UNIQUEIDENTIFIER NULL,
    created_at          DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at          DATETIME2 NOT NULL DEFAULT GETDATE(),

    CONSTRAINT UQ_delivery_pod_client UNIQUE (client_pod_id),
    CONSTRAINT FK_delivery_pod_trip_order FOREIGN KEY (trip_order_id)
        REFERENCES tenant.delivery_trip_orders(id),
    CONSTRAINT FK_delivery_pod_order FOREIGN KEY (sales_order_id)
        REFERENCES tenant.sales_orders(id),
    CONSTRAINT FK_delivery_pod_partner FOREIGN KEY (partner_id)
        REFERENCES tenant.partners(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_pod_trip_order' AND object_id = OBJECT_ID(N'tenant.delivery_pod'))
    CREATE INDEX idx_pod_trip_order ON tenant.delivery_pod(trip_order_id);
ELSE
    PRINT 'Bo qua index idx_pod_trip_order (da ton tai)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_pod_so' AND object_id = OBJECT_ID(N'tenant.delivery_pod'))
    CREATE INDEX idx_pod_so ON tenant.delivery_pod(sales_order_id);
ELSE
    PRINT 'Bo qua index idx_pod_so (da ton tai)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_pod_partner' AND object_id = OBJECT_ID(N'tenant.delivery_pod'))
    CREATE INDEX idx_pod_partner ON tenant.delivery_pod(partner_id);
ELSE
    PRINT 'Bo qua index idx_pod_partner (da ton tai)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_pod_gps' AND object_id = OBJECT_ID(N'tenant.delivery_pod'))
    CREATE INDEX idx_pod_gps ON tenant.delivery_pod(partner_id, gps_captured_at);
ELSE
    PRINT 'Bo qua index idx_pod_gps (da ton tai)';
PRINT 'OK: Bang delivery_pod da tao (co UQ_delivery_pod_client)';
GO

-- ============================================================
-- BANG 4: pod_items (Chi tiet POD - hang giao thuc te)
-- ============================================================
IF OBJECT_ID('tenant.pod_items', 'U') IS NULL
CREATE TABLE tenant.pod_items (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    pod_id              UNIQUEIDENTIFIER NOT NULL,
    sales_order_item_id UNIQUEIDENTIFIER NULL,
    product_id          UNIQUEIDENTIFIER NOT NULL,
    -- Snapshot ten san pham tai thoi diem giao (tranh sua doi danh muc)
    product_name        NVARCHAR(500) NULL,
    uom_code            VARCHAR(20) NOT NULL DEFAULT 'CAI',

    -- So luong
    ordered_qty         DECIMAL(15,3) NOT NULL DEFAULT 0,
    delivered_qty       DECIMAL(15,3) NOT NULL DEFAULT 0,
    returned_qty        DECIMAL(15,3) NOT NULL DEFAULT 0,   -- Hang rot/mang ve

    -- Gia
    unit_price          DECIMAL(15,2) NOT NULL DEFAULT 0,
    line_total          DECIMAL(15,2) NOT NULL DEFAULT 0,

    notes               NVARCHAR(255) NULL,

    CONSTRAINT FK_pod_items_pod FOREIGN KEY (pod_id)
        REFERENCES tenant.delivery_pod(id) ON DELETE CASCADE,
    CONSTRAINT FK_pod_items_order_item FOREIGN KEY (sales_order_item_id)
        REFERENCES tenant.sales_order_items(id),
    CONSTRAINT FK_pod_items_product FOREIGN KEY (product_id)
        REFERENCES tenant.products(id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_pod_items_pod' AND object_id = OBJECT_ID(N'tenant.pod_items'))
    CREATE INDEX idx_pod_items_pod ON tenant.pod_items(pod_id);
ELSE
    PRINT 'Bo qua index idx_pod_items_pod (da ton tai)';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_pod_items_product' AND object_id = OBJECT_ID(N'tenant.pod_items'))
    CREATE INDEX idx_pod_items_product ON tenant.pod_items(product_id);
ELSE
    PRINT 'Bo qua index idx_pod_items_product (da ton tai)';
PRINT 'OK: Bang pod_items da tao';
GO

-- ============================================================
-- BANG 5: pod_attachments (Media - 1 POD nhieu anh)
-- ★ D6: file luu tren dia, DB chi luu duong dan
-- ============================================================
IF OBJECT_ID('tenant.pod_attachments', 'U') IS NULL
CREATE TABLE tenant.pod_attachments (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    pod_id              UNIQUEIDENTIFIER NOT NULL,
    kind                VARCHAR(20) NOT NULL,
    -- 'SIGNATURE','HANG_ROT','FAILED_EVIDENCE'
    file_path           NVARCHAR(500) NOT NULL,
    content_type        VARCHAR(50) NOT NULL,
    size_bytes          INT NOT NULL DEFAULT 0,

    created_at          DATETIME2 NOT NULL DEFAULT GETDATE(),

    CONSTRAINT FK_pod_attachments_pod FOREIGN KEY (pod_id)
        REFERENCES tenant.delivery_pod(id) ON DELETE CASCADE
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_pod_attachments_pod' AND object_id = OBJECT_ID(N'tenant.pod_attachments'))
    CREATE INDEX idx_pod_attachments_pod ON tenant.pod_attachments(pod_id);
ELSE
    PRINT 'Bo qua index idx_pod_attachments_pod (da ton tai)';
PRINT 'OK: Bang pod_attachments da tao';
GO

PRINT '=========================================================';
PRINT ' 5 bang nghiep vu giao hang da tao';
PRINT ' - delivery_trips (TEMPORAL)';
PRINT ' - delivery_trip_orders';
PRINT ' - delivery_pod (co UQ_delivery_pod_client - chong double-POD)';
PRINT ' - pod_items';
PRINT ' - pod_attachments';
PRINT '=========================================================';
GO


-- ===== 05_seed_data.sql =====
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
