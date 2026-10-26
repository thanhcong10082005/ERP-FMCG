using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Tuyến giao hàng.
/// </summary>
[Table("routes", Schema = "tenant")]
public class Route : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string RouteCode { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string RouteName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string BusinessLine { get; set; } = "GENERAL";

    [MaxLength(255)]
    public string? Region { get; set; }

    [MaxLength(255)]
    public string? District { get; set; }

    public Guid? SupervisorId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime SysStartTime { get; set; }
    public DateTime SysEndTime { get; set; }

    public ICollection<Partner> Partners { get; set; } = new List<Partner>();
}
