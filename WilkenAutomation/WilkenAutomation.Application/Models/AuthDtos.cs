namespace WilkenAutomation.Application.Models;

// ---------------------------------------------------------------------------
// Authentication wire DTOs. Property names/shapes intentionally match the
// existing Angular frontend (frontend/src/app/core/models.ts). Do not rename
// without checking the frontend first.
// ---------------------------------------------------------------------------

public class RegisterRequestDto
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? DisplayName { get; set; }
}

public class LoginRequestDto
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public class AuthUserDto
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class AuthResponseDto
{
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    /// <summary>Omitted from HTTP JSON (httpOnly cookie). Present when issued by AuthService.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RefreshToken { get; set; }
    public DateTime RefreshExpiresAt { get; set; }
    public AuthUserDto User { get; set; } = default!;
}

public class RefreshRequestDto
{
    public string? RefreshToken { get; set; }
}

public class LogoutRequestDto
{
    public string? RefreshToken { get; set; }
}
