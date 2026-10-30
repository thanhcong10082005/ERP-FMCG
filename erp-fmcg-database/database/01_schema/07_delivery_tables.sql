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
