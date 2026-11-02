using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace ERP.FMC.Api.Policies;

/// <summary>
/// Yêu cầu người dùng có một trong các role được chỉ định.
/// KHÔNG phân biệt hoa/thường.
///
/// LÝ DO CẦN THIẾT:
///   - Cơ sở dữ liệu lưu role dạng UPPERCASE: ADMIN, MANAGER, ACCOUNTANT, SALES, WAREHOUSE.
///   - `[Authorize(Roles = "Admin,Manager")]` (RolesAuthorizationRequirement) so sánh
///     CHUỖI CÓ PHÂN BIỆT HOA/THƯỜNG ⇒ "ADMIN" != "Admin" ⇒ mọi endpoint quản trị
///     trả 403 dù tài khoản có role hợp lệ.
///   - Handler này chuẩn hoá về không phân biệt hoa/thường (case-insensitive),
///     nên policy khớp với dữ liệu thực tế trong DB.
/// </summary>
public sealed class RoleInAnyRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> AllowedRoles { get; }

    public RoleInAnyRequirement(params string[] allowedRoles)
    {
        AllowedRoles = allowedRoles ?? Array.Empty<string>();
    }
}

/// <summary>
/// Handler kiểm tra role từ claim. Đọc cả claim chuẩn (<c>ClaimTypes.Role</c>)
/// và claim tùy biến <c>"role"</c> để tương thích với cả hai dạng token.
/// </summary>
public sealed class RoleInAnyHandler : AuthorizationHandler<RoleInAnyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleInAnyRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        // Lấy toàn bộ giá trị claim role (có thể có nhiều claim)
        var roles = context.User.Claims
            .Where(c => c.Type == ClaimTypes.Role
                     || c.Type == "role"
                     || c.Type == "roles"
                     || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .ToList();

        foreach (var userRole in roles)
        {
            foreach (var allowed in requirement.AllowedRoles)
            {
                if (string.Equals(userRole?.Trim(), allowed?.Trim(),
                                  StringComparison.OrdinalIgnoreCase))
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }
        }

        return Task.CompletedTask;
    }
}
