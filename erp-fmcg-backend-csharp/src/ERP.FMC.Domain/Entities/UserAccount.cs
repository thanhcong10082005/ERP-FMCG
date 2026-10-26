using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Tài khoản người dùng.
/// </summary>
[Table("user_accounts", Schema = "tenant")]
public class UserAccount : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [Required, MaxLength(255)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Role { get; set; } = "SALES";

    [MaxLength(50)]
    public string? EmployeeCode { get; set; }

    public Guid? RouteId { get; set; }

    [ForeignKey(nameof(RouteId))]
    public Route? Route { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? LastLoginAt { get; set; }
}
