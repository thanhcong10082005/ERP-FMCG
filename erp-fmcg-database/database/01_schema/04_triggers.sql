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
            -- ★ PHASE 6A.3 FIX: NULLIF(...,0) gây NULL cho average_cost (NOT NULL)
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
