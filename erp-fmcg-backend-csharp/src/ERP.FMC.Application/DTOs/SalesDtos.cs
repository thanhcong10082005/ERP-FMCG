namespace ERP.FMC.Application.DTOs;

// ============ Sales Order DTOs ============
public class SalesOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string Status { get; set; } = "CONFIRMED";
    public string PaymentStatus { get; set; } = "UNPAID";
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string? Notes { get; set; }
    public List<SalesOrderItemDto> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class SalesOrderItemDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public Guid ProductId { get; set; }
    public string ProductSku { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string UomCode { get; set; } = "CAI";
    public decimal OrderedQty { get; set; }
    public decimal DeliveredQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineSubtotal { get; set; }
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }
}

public class CreateSalesOrderDto
{
    public Guid CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveryDate { get; set; }
    public string? Notes { get; set; }
    public List<CreateSalesOrderItemDto> Items { get; set; } = new();
}

public class CreateSalesOrderItemDto
{
    public Guid ProductId { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; } = 0;
    public string? Notes { get; set; }
}

public class UpdateSalesOrderDto
{
    public DateTime? DeliveryDate { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public decimal? PaidAmount { get; set; }
    public string? Notes { get; set; }
}

public class SalesOrderFilterDto
{
    public string? Search { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// ============ Inventory DTOs ============
public class InventoryBalanceDto
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public string? ProductName { get; set; }
    public decimal OnHand { get; set; }
    public decimal Reserved { get; set; }
    public decimal Available { get; set; }
    public decimal LastCost { get; set; }
    public DateTime LastMovementAt { get; set; }
}

public class StockLedgerDto
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public int MovementType { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceNumber { get; set; }
    public decimal Quantity { get; set; }
    public string UomCode { get; set; } = "CAI";
    public decimal UnitCost { get; set; }
    public DateTime TransactionDate { get; set; }
}

// ============ Price List DTOs ============
public class PriceListDto
{
    public Guid Id { get; set; }
    public string PriceListCode { get; set; } = string.Empty;
    public string PriceListName { get; set; } = string.Empty;
    public string ApplyTo { get; set; } = "ALL";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public Guid? ProductId { get; set; }
    public string? ProductSku { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsActive { get; set; }
}
