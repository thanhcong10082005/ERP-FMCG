using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ERP.FMC.Application.DTOs;

// ===================================================================
// AUTH DTOs
// ===================================================================

public class DevLoginRequestDto
{
    /// <summary>Guid của user (string để tránh mất precision với JS number).</summary>
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("remember_me")]
    public bool RememberMe { get; set; }
}

public class LoginRequestDto
{
    [Required]
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("remember_me")]
    public bool RememberMe { get; set; }
}

public class AuthResultDto
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("user")]
    public DriverUserDto User { get; set; } = new();
}

/// <summary>
/// Thông tin user trả về cho Driver App.
/// Tất cả ID là <see cref="string"/> (Guid) — quyết định D1.
/// </summary>
public class DriverUserDto
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("role_name")]
    public string RoleName { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = "DEFAULT";

    [JsonPropertyName("route_id")]
    public string? RouteId { get; set; }
}
