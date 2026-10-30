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
