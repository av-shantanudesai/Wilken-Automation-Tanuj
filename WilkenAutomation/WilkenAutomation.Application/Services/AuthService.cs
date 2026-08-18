using System.Net.Mail;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

public class AuthService
{
    private readonly IUserRepository _users;
    private readonly JwtTokenService _tokens;

    public AuthService(IUserRepository users, JwtTokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        ValidatePassword(request.Password);

        if (await _users.GetByEmailAsync(email, ct) is not null)
            throw new AuthException("EMAIL_TAKEN", "An account with this email already exists.");

        var user = new AppUser
        {
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? email.Split('@')[0]
                : request.DisplayName.Trim(),
            PasswordHash = PasswordHasher.Hash(request.Password),
            CreatedAt = DateTime.UtcNow
        };
        user = await _users.CreateAsync(user, ct);
        return ToResponse(user);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _users.GetByEmailAsync(email, ct);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            throw new AuthException("INVALID_CREDENTIALS", "Invalid email or password.");

        return ToResponse(user);
    }

    private AuthResponseDto ToResponse(AppUser user)
    {
        var (token, expires) = _tokens.CreateUserToken(user);
        return new AuthResponseDto
        {
            Token = token,
            ExpiresAt = expires,
            User = new AuthUserDto
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName
            }
        };
    }

    private static string NormalizeEmail(string? email)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email))
            throw new AuthException("INVALID_EMAIL", "Email is required.");
        try { _ = new MailAddress(email); }
        catch { throw new AuthException("INVALID_EMAIL", "Email is not valid."); }
        return email;
    }

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new AuthException("WEAK_PASSWORD", "Password must be at least 8 characters.");
    }
}

public class AuthException : Exception
{
    public string ErrorCode { get; }
    public AuthException(string errorCode, string message) : base(message) => ErrorCode = errorCode;
}
