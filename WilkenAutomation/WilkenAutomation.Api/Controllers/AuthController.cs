using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WilkenAutomation.Api.Auth;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly IUserRepository _users;

    public AuthController(AuthService auth, IUserRepository users)
    {
        _auth = auth;
        _users = users;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterRequestDto request, CancellationToken ct)
    {
        try
        {
            return await _auth.RegisterAsync(request, ct);
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.ErrorCode == "EMAIL_TAKEN" ? 409 : 400, new { message = ex.Message, errorCode = ex.ErrorCode });
        }
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto request, CancellationToken ct)
    {
        try
        {
            return await _auth.LoginAsync(request, ct);
        }
        catch (AuthException ex)
        {
            return Unauthorized(new { message = ex.Message, errorCode = ex.ErrorCode });
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(User.GetUserId(), ct);
        if (user is null) return Unauthorized();
        return new AuthUserDto { Id = user.Id, Email = user.Email, DisplayName = user.DisplayName };
    }
}
