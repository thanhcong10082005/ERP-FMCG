using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Kho hàng.
/// </summary>
[Table("warehouses", Schema = "tenant")]
public class Warehouse : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string WarehouseCode { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string WarehouseName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string WarehouseType { get; set; } = "MAIN";

    [MaxLength(500)]
    public string? Address { get; set; }

    public Guid? ManagerId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime SysStartTime { get; set; }
    public DateTime SysEndTime { get; set; }
}
