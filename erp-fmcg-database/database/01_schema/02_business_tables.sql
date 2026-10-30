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

    -- ★ Phase 5A.2 (DB-C11): retry logic (chuyển từ Postgres v13_1_3)
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
