using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WilkenAutomation.Api.Auth;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    public const string RefreshCookieName = "wilken_refresh";

    private readonly AuthService _auth;
    private readonly IUserRepository _users;
    private readonly IHostEnvironment _environment;

    public AuthController(AuthService auth, IUserRepository users, IHostEnvironment environment)
    {
        _auth = auth;
        _users = users;
        _environment = environment;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterRequestDto request, CancellationToken ct)
    {
        var response = await _auth.RegisterAsync(request, ClientIp(), ct);
        return SessionResult(response);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto request, CancellationToken ct)
    {
        var response = await _auth.LoginAsync(request, ClientIp(), ct);
        return SessionResult(response);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponseDto>> Refresh([FromBody] RefreshRequestDto? request, CancellationToken ct)
    {
        var raw = request?.RefreshToken ?? Request.Cookies[RefreshCookieName];
        var response = await _auth.RefreshAsync(raw, ClientIp(), ct);
        return SessionResult(response);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto? request, CancellationToken ct)
    {
        var raw = request?.RefreshToken ?? Request.Cookies[RefreshCookieName];
        await _auth.LogoutAsync(raw, ct);
        Response.Cookies.Delete(RefreshCookieName, CookieOptions());
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(User.GetRequiredUserId(), ct);
        if (user is null) return Unauthorized();
        return new AuthUserDto { Id = user.Id, Email = user.Email, DisplayName = user.DisplayName };
    }

    private ActionResult<AuthResponseDto> SessionResult(AuthResponseDto response)
    {
        if (!string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            var options = CookieOptions();
            options.Expires = response.RefreshExpiresAt;
            Response.Cookies.Append(RefreshCookieName, response.RefreshToken, options);
        }

        response.RefreshToken = null;
        return response;
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = !_environment.IsDevelopment() || Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        IsEssential = true
    };
}
