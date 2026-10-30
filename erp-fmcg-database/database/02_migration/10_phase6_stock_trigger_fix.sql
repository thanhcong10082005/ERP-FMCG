-- ===================================================================
-- Trigger: Sau khi INSERT stock_ledger → UPSERT inventory_balances
-- -------------------------------------------------------------------
-- ★ PHASE 6A.3 FIX — lỗi "average_cost NULL khi tồn về 0".
--
--   SYMPTOM (triệu chứng):
--     INSERT vào tenant.stock_ledger bị lỗi:
--       "Cannot insert the value NULL into column 'average_cost',
--        table 'tenant.inventory_balances'; column does not allow nulls. (515)"
--     -> mọi bút toán SALE khi số dư tồn về đúng 0 đều FAIL.
--
--   ROOT CAUSE (nguyên nhân gốc):
--     Công thức giá vốn bình quân:
--       (target.qty * target.avg_cost + net * avg_cost) / NULLIF(qty + net, 0)
--     Khi `qty + net = 0` (bán hết sạch), NULLIF trả NULL →
--     phép chia NULL → `average_cost` = NULL, vi phạm NOT NULL.
--     Ngoài ra `total_value` là PERSISTED computed column, nên NULL
--     còn lan sang cả giá trị tồn kho.
--
--   FIX (cách sửa):
--     1. Không dùng NULLIF; chia trực tiếp khi tồn còn > 0.
--     2. Tách 2 trường hợp:
--        - Tồn còn > 0  → tính giá vốn bình quân như cũ.
--        - Tồn = 0      → giữ nguyên `target.average_cost` (giá vốn
--          lịch sử) vì tồn = 0 thì giá vốn bình quân vô nghĩa.
--     3. Clamp `average_cost` >= 0 để không có giá vốn âm do bán lỗ.
--
--   PREVENTION (ngăn tái diễn):
--     - CHECK constraint: `average_cost >= 0` trên inventory_balances.
--     - Script `scripts/06_seed_opening_stock.py` kiểm tra ledger <-> balances
--       khớp sau mỗi lần nạp tồn.
--     - Trigger không được dùng OUTPUT/INSERT ... SELECT trực tiếp
--       (giới hạn SQL Server: bảng đích có trigger không được dùng OUTPUT).
-- ===================================================================

USE ERP_FMCG;
GO

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
            -- ★ FIX: không bao giờ để NULL (cột NOT NULL) — xem note đầu file.
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

-- ===================================================================
-- PREVENTION — CHECK constraint chặn giá vốn âm
-- ===================================================================
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_inventory_balance_avg_cost'
                 AND parent_object_id = OBJECT_ID(N'tenant.inventory_balances'))
BEGIN
    ALTER TABLE tenant.inventory_balances WITH CHECK
        ADD CONSTRAINT CK_inventory_balance_avg_cost CHECK (average_cost >= 0);
    PRINT 'OK: da them CHECK CK_inventory_balance_avg_cost';
END
ELSE
    PRINT 'OK: CHECK CK_inventory_balance_avg_cost da ton tai';
GO

PRINT 'OK: trigger trg_stockledger_after_insert da cap nhat (fix average_cost NULL)';
GO
