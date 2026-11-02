-- ===================================================================
-- Migration 12 — CHẶN TỒN KHO ÂM Ở TẦNG DB (hardening)
-- -------------------------------------------------------------------
--   Mục tiêu: "Constraint ở DB, không chỉ ở code". Code đã được sửa để không bao giờ tạo tồn âm,
--   nhưng nếu có đường ghi khác (import, script, SQL tay, service mới)
--   thì DB phải chặn được.
--
--   BỐI CẢNH (sự cố đã xảy ra thật — regression):
--     Smoke test E2E tạo POD giao 10 RCS SKU 10000854 (Aquafina) trong
--     khi `inventory_balances` của SKU đó có tồn = 0 (chưa có nguồn tồn
--     đầu kỳ). Bút toán SALE trừ 10 ⇒ tồn = -10 ⇒
--       - check OPS-06 (không còn tồn âm trong stock_ledger) FAIL
--       - check OPS-07 (không còn tồn âm trong inventory_balances) FAIL
--       - `quantity_available` âm ⇒ báo cáo tồn kho & cảnh báo sai
--
--   CÁCH SỬA:
--     1. CHECK constraint trên `inventory_balances.quantity_on_hand >= 0`.
--     2. Trigger `trg_stockledger_after_insert` được sửa để KHÔNG cho
--        phép giảm tồn xuống dưới 0: nếu bút toán làm tồn âm thì
--        RAISE ERROR ⇒ transaction rollback ⇒ không ghi được bút toán âm.
--        (Trigger vẫn là nơi DUY NHẤT cập nhật tồn kho — không nhân bản
--         nguồn sự thật.)
--
--   LƯU Ý: CHECK constraint được thêm SAU khi đã cân bằng dữ liệu âm
--   hiện có (bước 4) — vì SQL Server không cho ALTER TABLE ... WITH CHECK
--   khi dữ liệu đang vi phạm.
-- ===================================================================

USE ERP_FMCG;
GO

-- -------------------------------------------------------------------
-- 1. Cân bằng dữ liệu âm hiện có (nếu có) TRƯỚC khi thêm constraint.
--    Ghi bút toán OPENING bù về 0 cho từng cặp (kho, SKU) bị âm.
--    Script này IDEMPOTENT: chạy lại không nhân đôi.
-- -------------------------------------------------------------------
DECLARE @w UNIQUEIDENTIFIER, @pid UNIQUEIDENTIFIER, @uom VARCHAR(20);
DECLARE @net DECIMAL(18,4), @cost DECIMAL(18,2);

DECLARE neg_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT warehouse_id, product_id, uom_code, net_qty
    FROM (
        SELECT warehouse_id, product_id, uom_code,
               SUM(quantity_in - quantity_out) AS net_qty
        FROM tenant.stock_ledger
        GROUP BY warehouse_id, product_id, uom_code
        HAVING SUM(quantity_in - quantity_out) < 0
    ) x;

OPEN neg_cur;
FETCH NEXT FROM neg_cur INTO @w, @pid, @uom, @net;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Giá vốn tham chiếu: bảng giá đang hiệu lực, fallback base_price.
    SELECT TOP 1 @cost = pl.unit_price
    FROM tenant.price_lists pl
    WHERE pl.product_id = @pid AND pl.is_active = 1
    ORDER BY pl.effective_date DESC, pl.priority ASC;

    IF @cost IS NULL OR @cost <= 0
        SELECT @cost = ISNULL((SELECT p.base_price FROM tenant.products p WHERE p.id = @pid), 0);

    IF @cost < 0 SET @cost = 0;

    INSERT INTO tenant.stock_ledger (
        transaction_date, transaction_time, transaction_type,
        warehouse_id, product_id, uom_code,
        quantity_in, quantity_out, unit_cost,
        reference_type, reference_id, partner_id, notes,
        created_at, created_by
    )
    VALUES (
        CAST(GETDATE() AS DATE), GETDATE(), 'OPENING',
        @w, @pid, @uom,
        -@net, 0, @cost,
        'OPENING', NULL, NULL,
        N'can bang ton am truoc khi them CHECK constraint',
        GETDATE(), NULL
    );

    FETCH NEXT FROM neg_cur INTO @w, @pid, @uom, @net;
END

CLOSE neg_cur;
DEALLOCATE neg_cur;
GO

-- -------------------------------------------------------------------
-- 2. CHECK constraint trên inventory_balances
-- -------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_inventory_balance_qty_non_negative'
                 AND parent_object_id = OBJECT_ID(N'tenant.inventory_balances'))
BEGIN
    -- Nếu còn dữ liệu vi phạm (do trigger cũ đã chạy) thì bỏ constraint
    -- với WITH NOCHECK để không chặn việc deploy; trigger bên dưới vẫn
    -- chặn các lần ghi MỚI.
    IF EXISTS (SELECT 1 FROM tenant.inventory_balances WHERE quantity_on_hand < 0)
    BEGIN
        ALTER TABLE tenant.inventory_balances WITH NOCHECK
            ADD CONSTRAINT CK_inventory_balance_qty_non_negative
            CHECK (quantity_on_hand >= 0);
        PRINT '[WARN] da them CK_inventory_balance_qty_non_negative voi WITH NOCHECK (con du lieu am)';
    END
    ELSE
    BEGIN
        ALTER TABLE tenant.inventory_balances WITH CHECK
            ADD CONSTRAINT CK_inventory_balance_qty_non_negative
            CHECK (quantity_on_hand >= 0);
        PRINT 'OK: da them CK_inventory_balance_qty_non_negative';
    END
END
ELSE
    PRINT 'OK: CK_inventory_balance_qty_non_negative da ton tai';
GO

-- -------------------------------------------------------------------
-- 3. Trigger tồn kho — chặn việc giảm tồn xuống dưới 0
--    (giữ nguyên vai trò: nơi DUY NHẤT cập nhật inventory_balances)
-- -------------------------------------------------------------------
CREATE OR ALTER TRIGGER tenant.trg_stockledger_after_insert
ON tenant.stock_ledger
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    -- ★ MỚI: chặn bút toán làm tồn âm.
    --   Nếu có dòng nào trong `inserted` làm số dư của (kho, SKU) âm
    --   thì RAISE ERROR ⇒ transaction rollback ⇒ không ghi được bút
    --   toán âm. Đây là lớp bảo vệ CUỐI CÙNG ở tầng DB.
    IF EXISTS (
        SELECT 1
        FROM (
            SELECT
                i.warehouse_id,
                i.product_id,
                i.uom_code,
                SUM(i.quantity_in - i.quantity_out) AS net_delta,
                ISNULL(b.quantity_on_hand, 0) AS current_qty
            FROM inserted i
            LEFT JOIN tenant.inventory_balances b
              ON  b.warehouse_id = i.warehouse_id
              AND b.product_id   = i.product_id
              AND b.uom_code     = i.uom_code
            GROUP BY i.warehouse_id, i.product_id, i.uom_code,
                     b.quantity_on_hand
        ) d
        WHERE d.current_qty + d.net_delta < 0
    )
    BEGIN
        DECLARE @detail NVARCHAR(1000);
        SELECT @detail = STUFF((
            SELECT N'; ' + CONCAT(p.sku, ' ton=', d.current_qty, ' xuat=', d.net_delta)
            FROM (
                SELECT
                    i.warehouse_id,
                    i.product_id,
                    i.uom_code,
                    SUM(i.quantity_in - i.quantity_out) AS net_delta,
                    ISNULL(b.quantity_on_hand, 0) AS current_qty
                FROM inserted i
                LEFT JOIN tenant.inventory_balances b
                  ON  b.warehouse_id = i.warehouse_id
                  AND b.product_id   = i.product_id
                  AND b.uom_code     = i.uom_code
                GROUP BY i.warehouse_id, i.product_id, i.uom_code,
                         b.quantity_on_hand
            ) d
            JOIN tenant.products p ON p.id = d.product_id
            WHERE d.current_qty + d.net_delta < 0
            FOR XML PATH(''), TYPE
        ).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

        RAISERROR (
            N'Ton kho am: khong the xuat so luong vuot ton thuc te.%s',
            16, 1, @detail
        );
        RETURN;
    END

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
            -- ★ FIX: không bao giờ để NULL (cột NOT NULL).
            average_cost = CASE
                WHEN target.quantity_on_hand + source.net_quantity > 0
                 AND source.avg_cost IS NOT NULL AND source.avg_cost > 0
                THEN (target.quantity_on_hand * target.average_cost
                      + source.net_quantity * source.avg_cost)
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

PRINT 'OK: migration 12 - chan ton kho am da cap nhat';
GO
