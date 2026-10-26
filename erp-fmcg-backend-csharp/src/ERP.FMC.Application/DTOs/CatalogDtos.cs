namespace ERP.FMC.Application.DTOs;

// ============ Product DTOs ============
public class ProductDto
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? Brand { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string BaseUom { get; set; } = "CAI";
    public decimal BasePrice { get; set; }
    public decimal VatRate { get; set; }
    public string? Barcode { get; set; }
    public decimal WeightGram { get; set; }
    public decimal VolumeMl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateProductDto
{
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? Brand { get; set; }
    public Guid? CategoryId { get; set; }
    public string BaseUom { get; set; } = "CAI";
    public decimal BasePrice { get; set; }
    public decimal VatRate { get; set; } = 8.0m;
    public string? Barcode { get; set; }
    public decimal WeightGram { get; set; }
    public decimal VolumeMl { get; set; }
}

public class UpdateProductDto
{
    public string ProductName { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? Brand { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal BasePrice { get; set; }
    public decimal VatRate { get; set; }
    public bool IsActive { get; set; }
}

public class ProductFilterDto
{
    public string? Search { get; set; }
    public string? Brand { get; set; }
    public Guid? CategoryId { get; set; }
    public bool? IsActive { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// ============ Warehouse DTOs ============
public class WarehouseDto
{
    public Guid Id { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string WarehouseType { get; set; } = "MAIN";
    public string? Address { get; set; }
    public bool IsActive { get; set; }
}

public class CreateWarehouseDto
{
    public string WarehouseCode { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string WarehouseType { get; set; } = "MAIN";
    public string? Address { get; set; }
}

// ============ Route DTOs ============
public class RouteDto
{
    public Guid Id { get; set; }
    public string RouteCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string BusinessLine { get; set; } = "GENERAL";
    public string? Region { get; set; }
    public int PartnerCount { get; set; }
    public bool IsActive { get; set; }
}

public class CreateRouteDto
{
    public string RouteCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string BusinessLine { get; set; } = "GENERAL";
    public string? Region { get; set; }
}

// ============ Category DTOs ============
public class CategoryDto
{
    public Guid Id { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}
