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

    public decimal CurrentDebt { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime SysStartTime { get; set; }
    public DateTime SysEndTime { get; set; }
}
