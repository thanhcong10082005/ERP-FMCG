using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.FMC.Api.Controllers;

// ===================================================================
// CONTROLLER XÁC THỰC
//
//  POST /api/auth/dev-login   đăng nhập nhanh (Driver App)
//  POST /api/auth/login       đăng nhập email/username + mật khẩu
//  GET  /api/auth/me          thông tin user hiện tại
// ===================================================================

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService) => _authService = authService;

    /// <summary>
    /// Đăng nhập nhanh cho Driver App.
    /// Body: { "user_id": "<guid>" } | { "email": "..." } | { "phone": "..." }
    /// </summary>
    [HttpPost("dev-login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResultDto>> DevLogin(
        [FromBody] DevLoginRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { error = "VALIDATION_ERROR", message = "Dữ liệu không hợp lệ." });

        var result = await _authService.DevLoginAsync(request, ct);
        return Ok(result);
    }

    /// <summary>Đăng nhập bằng email/username + mật khẩu.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResultDto>> Login(
        [FromBody] LoginRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { error = "VALIDATION_ERROR", message = "Dữ liệu không hợp lệ." });

        var result = await _authService.LoginAsync(request, ct);
        return Ok(result);
    }

    /// <summary>Thông tin user hiện tại (từ JWT).</summary>
    [HttpGet("me")]
    [Authorize]
    public ActionResult<object> Me()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var fullName = User.FindFirst("full_name")?.Value;
        var role = User.FindFirst("role")?.Value;

        return Ok(new
        {
            user_id = userId,
            full_name = fullName,
            role = role,
        });
    }
}
