using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Khách hàng / đối tác.
/// </summary>
[Table("partners", Schema = "tenant")]
public class Partner : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string PartnerCode { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string PartnerName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string PartnerType { get; set; } = "STORE"; // STORE, ASO, CORPORATE

    [MaxLength(20)]
    public string? TaxCode { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? Ward { get; set; }

    [MaxLength(100)]
    public string? District { get; set; }

    [MaxLength(100)]
    public string? Province { get; set; }

    // tọa độ GPS (đã có sẵn trong schema, trước đây entity thiếu)
    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    [MaxLength(50)]
    public string? RouteCode { get; set; }

    public Guid? RouteId { get; set; }

    [ForeignKey(nameof(RouteId))]
    public Route? Route { get; set; }

    [MaxLength(50)]
    public string? DcrCode { get; set; }

    [MaxLength(50)]
    public string? OutletTypeCode { get; set; }

    [MaxLength(10)]
    public string? Segment { get; set; }

    public decimal CreditLimit { get; set; } = 0;

    /// <summary>
    /// công nợ HIỆN TẠI chưa thu
    ///   (đơn mới ghi nợ + công nợ cũ được chuyển sang).
    /// </summary>
    public decimal CurrentDebt { get; set; } = 0;

    /// <summary>
    /// công nợ CŨ chưa thu.
    ///   Nạp từ file gốc (480 khách trùng DB) và giảm dần khi tài xế thu được.
    /// </summary>
    public decimal OldDebt { get; set; } = 0;

    public DateTime? OldDebtDate { get; set; }

    // metadata geocoding
    //   LOW / MEDIUM / HIGH = auto-geocoder; MANUAL = admin sửa tay
    [MaxLength(10)]
    public string? GeocodingConfidence { get; set; }

    public DateTime? LastGeocodedAt { get; set; }

    [MaxLength(500)]
    public string? GeocodedAddress { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime SysStartTime { get; set; }
    public DateTime SysEndTime { get; set; }
}
