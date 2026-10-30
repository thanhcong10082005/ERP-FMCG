using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

namespace ERP.FMC.Application.Services;

/// <summary>
/// Cấu hình JWT.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Khoá ký. KHÔNG commit giá trị thật — dùng User Secrets / appsettings.Local.json.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "ERP-FMCG";

    public string Audience { get; set; } = "ERP-FMCG-Driver";

    public int ExpireMinutes { get; set; } = 720; // 12 giờ — ca giao hàng

    public int ExpireMinutesRememberMe { get; set; } = 4320; // 3 ngày
}

/// <summary>
/// Sinh và xác thực JWT.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Sinh access token cho tài xế.</summary>
    (string Token, DateTime ExpiresAt) GenerateToken(UserAccount user, bool rememberMe = false);

    /// <summary>Sinh refresh token (random, lưu ở client).</summary>
    string GenerateRefreshToken();

    /// <summary>Lấy user hiện tại từ ClaimsPrincipal.</summary>
    Guid? GetCurrentUserId(ClaimsPrincipal? principal);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly ILogger<JwtTokenService> _logger;

    public JwtTokenService(JwtOptions options, ILogger<JwtTokenService> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Lấy khoá ký từ cấu hình. Nếu không có, sinh khoá tạm cho môi trường
    /// Development để API vẫn chạy được, nhưng KHÔNG dùng được ở Production.
    /// </summary>
    public SymmetricSecurityKey GetSigningKey()
    {
        var key = _options.SigningKey;
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("ERP_FMCG_JWT_KEY");

        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning(
                "JWT:SigningKey chua duoc cau hinh. Dung khoa tam cho Development. " +
                "Production BAT BUOC dat appsettings.Local.json hoac bien moi truong ERP_FMCG_JWT_KEY.");
            key = "DEV-ONLY-INSECURE-KEY-CHANG-DOI-KEY-NAY-TRUOC-KHI-LEN-PROD-0123456789";
        }

        // HS256 yeu cau >= 256 bit
        var bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException(
                $"JWT:SigningKey phai >= 32 byte (256 bit) cho HS256, hien chi co {bytes.Length} byte.");
        }

        return new SymmetricSecurityKey(bytes);
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(UserAccount user, bool rememberMe = false)
    {
        var expires = DateTime.UtcNow.AddMinutes(
            rememberMe ? _options.ExpireMinutesRememberMe : _options.ExpireMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("full_name", user.FullName),
            new("role", user.Role),
            new("tenant_id", "DEFAULT"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };

        if (user.RouteId.HasValue)
            claims.Add(new Claim("route_id", user.RouteId.Value.ToString()));

        var creds = new SigningCredentials(
            GetSigningKey(), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public Guid? GetCurrentUserId(ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? principal?.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>
/// Đăng nhập cho Driver App.
/// </summary>
public interface IAuthService
{
    Task<AuthResultDto> DevLoginAsync(DevLoginRequestDto request, CancellationToken ct = default);
    Task<AuthResultDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default);
}

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _uow;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IUnitOfWork uow, IJwtTokenService jwt, ILogger<AuthService> logger)
    {
        _uow = uow;
        _jwt = jwt;
        _logger = logger;
    }

    /// <summary>
    /// Đăng nhập "dev" — dùng cho Driver App khi chưa có SSO.
    /// Chấp nhận: user_id (Guid), email, username hoặc phone.
    /// </summary>
    public async Task<AuthResultDto> DevLoginAsync(DevLoginRequestDto request, CancellationToken ct = default)
    {
        UserAccount? user = null;

        if (!string.IsNullOrWhiteSpace(request.UserId) && Guid.TryParse(request.UserId, out var uid))
        {
            user = await _uow.UserAccounts.GetByIdAsync(uid);
        }

        if (user is null && !string.IsNullOrWhiteSpace(request.Email))
        {
            var list = await _uow.UserAccounts.GetAllAsync();
            user = list.FirstOrDefault(u =>
                string.Equals(u.Email, request.Email, StringComparison.OrdinalIgnoreCase));
        }

        if (user is null && !string.IsNullOrWhiteSpace(request.Username))
        {
            var list = await _uow.UserAccounts.GetAllAsync();
            user = list.FirstOrDefault(u =>
                string.Equals(u.Username, request.Username, StringComparison.OrdinalIgnoreCase));
        }

        if (user is null && !string.IsNullOrWhiteSpace(request.Phone))
        {
            var list = await _uow.UserAccounts.GetAllAsync();
            user = list.FirstOrDefault(u =>
                string.Equals(u.Phone, request.Phone, StringComparison.OrdinalIgnoreCase));
        }

        if (user is null)
            throw new KeyNotFoundException("Không tìm thấy tài khoản");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Tài khoản đã bị vô hiệu hoá");

        return BuildResult(user, request.RememberMe);
    }

    /// <summary>Đăng nhập bằng email/username + mật khẩu.</summary>
    public async Task<AuthResultDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default)
    {
        var list = await _uow.UserAccounts.GetAllAsync();
        var user = list.FirstOrDefault(u =>
            string.Equals(u.Email, request.Email, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.Username, request.Email, StringComparison.OrdinalIgnoreCase));

        if (user is null)
            throw new KeyNotFoundException("Không tìm thấy tài khoản");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Tài khoản đã bị vô hiệu hoá");

        //  xác thực mật khẩu bằng BCrypt khi có cột password_hash
        // hiện user_accounts chưa có password_hash trong schema MS SQL.
        if (!string.IsNullOrWhiteSpace(request.Password) && string.IsNullOrEmpty(user.PasswordHash))
            throw new UnauthorizedAccessException("Tài khoản chưa có mật khẩu. Dùng dev-login.");

        return BuildResult(user, request.RememberMe);
    }

    private AuthResultDto BuildResult(UserAccount user, bool rememberMe)
    {
        var (token, expiresAt) = _jwt.GenerateToken(user, rememberMe);
        return new AuthResultDto
        {
            AccessToken = token,
            RefreshToken = _jwt.GenerateRefreshToken(),
            ExpiresAt = expiresAt,
            User = new DriverUserDto
            {
                UserId = user.Id.ToString(),
                Username = user.Username,
                FullName = user.FullName,
                Phone = user.Phone ?? string.Empty,
                Email = user.Email ?? string.Empty,
                Role = user.Role,
                RoleName = user.Role,
                TenantId = "DEFAULT",
                RouteId = user.RouteId?.ToString(),
            },
        };
    }
}
