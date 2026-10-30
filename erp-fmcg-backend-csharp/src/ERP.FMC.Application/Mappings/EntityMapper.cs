using ERP.FMC.Application.DTOs;
using ERP.FMC.Domain.Entities;

namespace ERP.FMC.Application.Mappings;

// ===================================================================
// ★ PHASE 6C — THAY THẾ AutoMapper BẰNG MAPPING THỦ CÔNG
// -------------------------------------------------------------------
// Lý do (6C — NuGet hardening):
//   AutoMapper 12.0.1 có lỗ hổng DoS (GHSA-rvv3-g6hj-g44x) — đệ quy
//   không giới hạn độ sâu ⇒ StackOverflowException KHÔNG bắt được ⇒
//   giết toàn bộ process.
//
//   Bản vá lỗ hổng là AutoMapper >= 15.1.1, NHƯNG từ bản 15 AutoMapper
//   chuyển sang mô hình thương mại: cần license key khi doanh thu > $5M/năm.
//   => Không phù hợp với dự án nội bộ này.
//
//   Quyết định: VIẾT TAY toàn bộ mapping. DTO này chỉ có ~15 kiểu và
//   mapping tĩnh, không cần reflection runtime.
//
//   LỢI ÍCH:
//   - Không còn cảnh báo NU1903 trong CI.
//   - Không cần license key.
//   - Mapping là mã nguồn thuần → IDE hiểu, dễ review, dễ test.
//   - Nhanh hơn (không có cache expression runtime).
// ===================================================================

public static class EntityMapper
{
    // -------------------------------------------------------------------
    // Partner
    // -------------------------------------------------------------------
    public static PartnerDto ToDto(this Partner p) => new()
    {
        Id = p.Id,
        PartnerCode = p.PartnerCode,
        PartnerName = p.PartnerName,
        PartnerType = p.PartnerType,
        Phone = p.Phone,
        Email = p.Email,
        Address = p.Address,
        Ward = p.Ward,
        District = p.District,
        Province = p.Province,
        RouteCode = p.RouteCode,
        RouteId = p.RouteId,
        OutletTypeCode = p.OutletTypeCode,
        CreditLimit = p.CreditLimit,
        CurrentDebt = p.CurrentDebt,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt,
    };

    public static Partner ToEntity(this CreatePartnerDto d) => new()
    {
        PartnerCode = d.PartnerCode,
        PartnerName = d.PartnerName,
        PartnerType = d.PartnerType,
        Phone = d.Phone,
        Email = d.Email,
        Address = d.Address,
        Ward = d.Ward,
        District = d.District,
        Province = d.Province,
        RouteCode = d.RouteCode,
        OutletTypeCode = d.OutletTypeCode,
        CreditLimit = d.CreditLimit,
        IsActive = true,
    };

    public static void ApplyTo(this UpdatePartnerDto d, Partner p)
    {
        p.PartnerName = d.PartnerName;
        p.Phone = d.Phone;
        p.Email = d.Email;
        p.Address = d.Address;
        p.RouteCode = d.RouteCode;
        p.OutletTypeCode = d.OutletTypeCode;
        p.CreditLimit = d.CreditLimit;
        p.IsActive = d.IsActive;
        p.UpdatedAt = DateTime.UtcNow;
    }

    // -------------------------------------------------------------------
    // Product
    // -------------------------------------------------------------------
    public static ProductDto ToDto(this Product p) => new()
    {
        Id = p.Id,
        Sku = p.Sku,
        ProductName = p.ProductName,
        ShortName = p.ShortName,
        Brand = p.Brand,
        CategoryId = p.CategoryId,
        CategoryName = p.Category?.CategoryName,
        BaseUom = p.BaseUom,
        BasePrice = p.BasePrice,
        VatRate = p.VatRate,
        Barcode = p.Barcode,
        WeightGram = p.WeightGram ?? 0,
        VolumeMl = p.VolumeMl ?? 0,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt,
    };

    public static Product ToEntity(this CreateProductDto d) => new()
    {
        Sku = d.Sku,
        ProductName = d.ProductName,
        ShortName = d.ShortName,
        Brand = d.Brand,
        CategoryId = d.CategoryId,
        BaseUom = d.BaseUom,
        BasePrice = d.BasePrice,
        VatRate = d.VatRate,
        Barcode = d.Barcode,
        WeightGram = d.WeightGram,
        VolumeMl = d.VolumeMl,
        IsActive = true,
    };

    public static void ApplyTo(this UpdateProductDto d, Product p)
    {
        p.ProductName = d.ProductName;
        p.ShortName = d.ShortName;
        p.Brand = d.Brand;
        p.CategoryId = d.CategoryId;
        p.BasePrice = d.BasePrice;
        p.VatRate = d.VatRate;
        p.IsActive = d.IsActive;
        p.UpdatedAt = DateTime.UtcNow;
    }

    // -------------------------------------------------------------------
    // Warehouse
    // -------------------------------------------------------------------
    public static WarehouseDto ToDto(this Warehouse w) => new()
    {
        Id = w.Id,
        WarehouseCode = w.WarehouseCode,
        WarehouseName = w.WarehouseName,
        WarehouseType = w.WarehouseType,
        Address = w.Address,
        IsActive = w.IsActive,
    };

    public static Warehouse ToEntity(this CreateWarehouseDto d) => new()
    {
        WarehouseCode = d.WarehouseCode,
        WarehouseName = d.WarehouseName,
        WarehouseType = d.WarehouseType,
        Address = d.Address,
        IsActive = true,
    };

    // -------------------------------------------------------------------
    // Route
    // -------------------------------------------------------------------
    public static RouteDto ToDto(this Route r) => new()
    {
        Id = r.Id,
        RouteCode = r.RouteCode,
        RouteName = r.RouteName,
        BusinessLine = r.BusinessLine,
        Region = r.Region,
        PartnerCount = r.Partners?.Count ?? 0,
        IsActive = r.IsActive,
    };

    public static Route ToEntity(this CreateRouteDto d) => new()
    {
        RouteCode = d.RouteCode,
        RouteName = d.RouteName,
        BusinessLine = d.BusinessLine,
        Region = d.Region,
        IsActive = true,
    };

    // -------------------------------------------------------------------
    // Category
    // -------------------------------------------------------------------
    public static CategoryDto ToDto(this Category c) => new()
    {
        Id = c.Id,
        CategoryCode = c.CategoryCode,
        CategoryName = c.CategoryName,
        ParentId = c.ParentId,
        SortOrder = c.SortOrder,
        IsActive = c.IsActive,
    };

    public static Category ToEntity(this CategoryDto d) => new()
    {
        CategoryCode = d.CategoryCode,
        CategoryName = d.CategoryName,
        ParentId = d.ParentId,
        SortOrder = d.SortOrder,
        IsActive = d.IsActive,
    };

    // -------------------------------------------------------------------
    // Sales Order
    // -------------------------------------------------------------------
    public static SalesOrderDto ToDto(this SalesOrder o) => new()
    {
        Id = o.Id,
        OrderNumber = o.OrderNumber,
        CustomerId = o.CustomerId,
        CustomerCode = o.Customer?.PartnerCode,
        CustomerName = o.Customer?.PartnerName,
        WarehouseId = o.WarehouseId,
        WarehouseName = o.Warehouse?.WarehouseName,
        OrderDate = o.OrderDate,
        DeliveryDate = o.DeliveryDate,
        Status = o.Status,
        PaymentStatus = o.PaymentStatus,
        Subtotal = o.Subtotal,
        DiscountAmount = o.DiscountAmount,
        TaxAmount = o.TaxAmount,
        TotalAmount = o.TotalAmount,
        PaidAmount = o.PaidAmount,
        Notes = o.Notes,
        CreatedAt = o.CreatedAt,
    };

    public static SalesOrder ToEntity(this CreateSalesOrderDto d) => new()
    {
        CustomerId = d.CustomerId,
        WarehouseId = d.WarehouseId,
        OrderDate = d.OrderDate,
        DeliveryDate = d.DeliveryDate,
        Notes = d.Notes,
        Status = "CONFIRMED",
        PaymentStatus = "UNPAID",
    };

    // -------------------------------------------------------------------
    // Sales Order Item
    // -------------------------------------------------------------------
    public static SalesOrderItemDto ToDto(this SalesOrderItem i) => new()
    {
        Id = i.Id,
        LineNumber = i.LineNumber,
        ProductId = i.ProductId,
        ProductSku = i.ProductSku,
        ProductName = i.Product?.ProductName,
        UomCode = i.UomCode,
        OrderedQty = i.OrderedQty,
        DeliveredQty = i.DeliveredQty,
        UnitPrice = i.UnitPrice,
        DiscountAmount = i.DiscountAmount,
        LineSubtotal = i.LineSubtotal,
        LineTotal = i.LineTotal,
        Notes = i.Notes,
    };

    public static SalesOrderItem ToEntity(this CreateSalesOrderItemDto d, int lineNumber) => new()
    {
        LineNumber = lineNumber,
        ProductId = d.ProductId,
        OrderedQty = d.OrderedQty,
        UnitPrice = d.UnitPrice,
        DiscountPercent = d.DiscountPercent,
        Notes = d.Notes,
    };

    // -------------------------------------------------------------------
    // Inventory Balance
    // -------------------------------------------------------------------
    public static InventoryBalanceDto ToDto(this InventoryBalance b) => new()
    {
        Id = b.Id,
        WarehouseId = b.WarehouseId,
        WarehouseName = b.Warehouse?.WarehouseName,
        ProductId = b.ProductId,
        ProductSku = b.Product?.Sku,
        ProductName = b.Product?.ProductName,
        OnHand = b.OnHand,
        Reserved = b.Reserved,
        Available = b.Available ?? 0,
        LastCost = b.LastCost,
        LastMovementAt = b.LastMovementAt ?? DateTime.MinValue,
    };

    // -------------------------------------------------------------------
    // Stock Ledger
    // -------------------------------------------------------------------
    public static StockLedgerDto ToDto(this StockLedger l) => new()
    {
        Id = l.Id,
        WarehouseId = l.WarehouseId,
        WarehouseName = l.Warehouse?.WarehouseName,
        ProductId = l.ProductId,
        ProductSku = l.Product?.Sku,
        MovementType = (int)l.MovementType,
        ReferenceType = l.ReferenceType,
        ReferenceNumber = l.ReferenceNumber,
        Quantity = l.Quantity,
        UomCode = l.UomCode,
        UnitCost = l.UnitCost ?? 0,
        TransactionDate = l.TransactionDate,
    };

    // -------------------------------------------------------------------
    // Price List
    // -------------------------------------------------------------------
    public static PriceListDto ToDto(this PriceList p) => new()
    {
        Id = p.Id,
        PriceListCode = p.PriceListCode,
        PriceListName = p.PriceListName,
        ApplyTo = p.ApplyTo,
        EffectiveFrom = p.EffectiveFrom,
        EffectiveTo = p.EffectiveTo,
        ProductId = p.ProductId,
        ProductSku = p.Product?.Sku,
        UnitPrice = p.UnitPrice,
        IsActive = p.IsActive,
    };

    public static PriceList ToEntity(this PriceListDto d) => new()
    {
        PriceListCode = d.PriceListCode,
        PriceListName = d.PriceListName,
        ApplyTo = d.ApplyTo,
        EffectiveFrom = d.EffectiveFrom,
        EffectiveTo = d.EffectiveTo,
        ProductId = d.ProductId,
        UnitPrice = d.UnitPrice,
        IsActive = d.IsActive,
    };
}
