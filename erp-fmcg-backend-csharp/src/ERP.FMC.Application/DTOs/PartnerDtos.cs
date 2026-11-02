namespace ERP.FMC.Application.DTOs;

// ============ Partner DTOs ============
public class PartnerDto
{
    public Guid Id { get; set; }
    public string PartnerCode { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public string PartnerType { get; set; } = "STORE";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Ward { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
    public string? RouteCode { get; set; }
    public Guid? RouteId { get; set; }
    public string? OutletTypeCode { get; set; }
    public decimal CreditLimit { get; set; }

    /// <summary>Công nợ hiện tại (nợ chưa thu). Âm = khách đã trả trước.</summary>
    public decimal CurrentDebt { get; set; }

    /// <summary>
    /// Công nợ cũ (nợ chưa thu từ kỳ trước).
    /// Âm = khách đã trả trước (số dư dương của ERP) — xem
    /// <c>DebtSignConventions</c>.
    /// </summary>
    public decimal OldDebt { get; set; }

    /// <summary>Ngày ghi nhận công nợ cũ (dùng cho báo cáo tuổi nợ).</summary>
    public DateTime? OldDebtDate { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePartnerDto
{
    public string PartnerCode { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public string PartnerType { get; set; } = "STORE";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Ward { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
    public string? RouteCode { get; set; }
    public string? OutletTypeCode { get; set; }
    public decimal CreditLimit { get; set; }
}

public class UpdatePartnerDto
{
    public string PartnerName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? RouteCode { get; set; }
    public string? OutletTypeCode { get; set; }
    public decimal CreditLimit { get; set; }
    public bool IsActive { get; set; }
}

public class PartnerFilterDto
{
    public string? Search { get; set; }
    public string? PartnerType { get; set; }
    public string? RouteCode { get; set; }
    public string? Segment { get; set; }
    public bool? IsActive { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? OrderBy { get; set; } = "PartnerCode";
}
