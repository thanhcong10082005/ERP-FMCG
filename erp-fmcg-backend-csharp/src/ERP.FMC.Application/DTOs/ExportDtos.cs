namespace ERP.FMC.Application.DTOs;

// ============ Export DTOs ============
public class ExportRequestDto
{
    public string ExportType { get; set; } = string.Empty;
    public string Format { get; set; } = "XLSX";
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? Status { get; set; }
    public List<string>? Columns { get; set; }
    public bool IncludeHeaders { get; set; } = true;
    public string? FileName { get; set; }
    public bool? IsActive { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public class ExportResultDto
{
    public Guid ExportId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int RowCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
}

public class ExportColumnDto
{
    public string Field { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public string? Format { get; set; }  // date, currency, number
    public int Width { get; set; } = 15;
    public bool IsVisible { get; set; } = true;
}

public class ExportTemplateDto
{
    public string ExportType { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<ExportColumnDto> Columns { get; set; } = new();
    public List<ExportFilterDto> Filters { get; set; } = new();
}

// ============ Export Templates ============
public class ExportFilterDto
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "eq";  // eq, ne, gt, lt, contains
    public string? Value { get; set; }
}

public static class ExportTemplates
{
    public static ExportTemplateDto PartnersTemplate => new()
    {
        ExportType = "PARTNERS",
        TemplateName = "Danh sách khách hàng",
        Description = "Export toàn bộ khách hàng với thông tin liên hệ",
        Columns = new()
        {
            new() { Field = "PartnerCode", Header = "Mã KH", Width = 15 },
            new() { Field = "PartnerName", Header = "Tên khách hàng", Width = 30 },
            new() { Field = "PartnerType", Header = "Loại", Width = 12 },
            new() { Field = "Phone", Header = "Điện thoại", Width = 15 },
            new() { Field = "Email", Header = "Email", Width = 25 },
            new() { Field = "Address", Header = "Địa chỉ", Width = 40 },
            new() { Field = "RouteCode", Header = "Mã tuyến", Width = 12 },
            new() { Field = "CreditLimit", Header = "Hạn mức", Width = 15, Format = "#,##0" },
            new() { Field = "IsActive", Header = "Hoạt động", Width = 10 }
        }
    };

    public static ExportTemplateDto ProductsTemplate => new()
    {
        ExportType = "PRODUCTS",
        TemplateName = "Danh sách sản phẩm",
        Description = "Export toàn bộ sản phẩm với giá",
        Columns = new()
        {
            new() { Field = "Sku", Header = "SKU", Width = 15 },
            new() { Field = "ProductName", Header = "Tên sản phẩm", Width = 35 },
            new() { Field = "ShortName", Header = "Tên ngắn", Width = 20 },
            new() { Field = "Brand", Header = "Nhãn hiệu", Width = 15 },
            new() { Field = "BaseUom", Header = "ĐVT", Width = 8 },
            new() { Field = "BasePrice", Header = "Giá bán", Width = 15, Format = "#,##0" },
            new() { Field = "VatRate", Header = "VAT%", Width = 8, Format = "0%" },
            new() { Field = "WeightGram", Header = "Trọng lượng(g)", Width = 12 },
            new() { Field = "VolumeMl", Header = "Thể tích(ml)", Width = 12 },
            new() { Field = "IsActive", Header = "Hoạt động", Width = 10 }
        }
    };

    public static ExportTemplateDto OrdersTemplate => new()
    {
        ExportType = "ORDERS",
        TemplateName = "Báo cáo đơn hàng",
        Description = "Export đơn hàng theo khoảng thời gian",
        Columns = new()
        {
            new() { Field = "OrderNumber", Header = "Số đơn", Width = 20 },
            new() { Field = "OrderDate", Header = "Ngày đặt", Width = 12, Format = "dd/MM/yyyy" },
            new() { Field = "CustomerCode", Header = "Mã KH", Width = 15 },
            new() { Field = "CustomerName", Header = "Khách hàng", Width = 30 },
            new() { Field = "Status", Header = "Trạng thái", Width = 12 },
            new() { Field = "PaymentStatus", Header = "TT thanh toán", Width = 15 },
            new() { Field = "Subtotal", Header = "T.Subtotal", Width = 15, Format = "#,##0" },
            new() { Field = "DiscountAmount", Header = "Chiết khấu", Width = 12, Format = "#,##0" },
            new() { Field = "TaxAmount", Header = "Thuế", Width = 12, Format = "#,##0" },
            new() { Field = "TotalAmount", Header = "Tổng cộng", Width = 18, Format = "#,##0" },
            new() { Field = "PaidAmount", Header = "Đã thanh toán", Width = 15, Format = "#,##0" },
            new() { Field = "DeliveryDate", Header = "Ngày giao", Width = 12, Format = "dd/MM/yyyy" },
            new() { Field = "Notes", Header = "Ghi chú", Width = 30 }
        }
    };

    public static ExportTemplateDto InventoryTemplate => new()
    {
        ExportType = "INVENTORY",
        TemplateName = "Báo cáo tồn kho",
        Description = "Export số dư tồn kho theo kho",
        Columns = new()
        {
            new() { Field = "WarehouseName", Header = "Kho", Width = 20 },
            new() { Field = "ProductSku", Header = "SKU", Width = 15 },
            new() { Field = "ProductName", Header = "Sản phẩm", Width = 35 },
            new() { Field = "OnHand", Header = "Tồn", Width = 12, Format = "#,##0" },
            new() { Field = "Reserved", Header = "Đã hủy", Width = 12, Format = "#,##0" },
            new() { Field = "Available", Header = "Khả dụng", Width = 12, Format = "#,##0" },
            new() { Field = "LastCost", Header = "Giá cuối", Width = 15, Format = "#,##0" },
            new() { Field = "LastMovementAt", Header = "Lần cuối nhập/xuất", Width = 18, Format = "dd/MM/yyyy HH:mm" }
        }
    };

    public static ExportTemplateDto RevenueTemplate => new()
    {
        ExportType = "REVENUE",
        TemplateName = "Báo cáo doanh thu",
        Description = "Tổng hợp doanh thu theo tháng/khách hàng/sản phẩm",
        Columns = new()
        {
            new() { Field = "Period", Header = "Kỳ", Width = 12 },
            new() { Field = "CustomerCode", Header = "Mã KH", Width = 15 },
            new() { Field = "CustomerName", Header = "Khách hàng", Width = 30 },
            new() { Field = "OrderCount", Header = "Số đơn", Width = 10, Format = "#,##0" },
            new() { Field = "TotalAmount", Header = "Doanh thu", Width = 20, Format = "#,##0" },
            new() { Field = "PaidAmount", Header = "Đã thu", Width = 18, Format = "#,##0" },
            new() { Field = "Outstanding", Header = "Còn lại", Width = 18, Format = "#,##0" }
        }
    };
}
