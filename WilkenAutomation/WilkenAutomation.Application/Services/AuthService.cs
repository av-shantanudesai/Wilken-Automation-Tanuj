using System.Net.Mail;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

public class AuthService
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly JwtTokenService _tokens;

    public AuthService(IUserRepository users, IRefreshTokenRepository refreshTokens, JwtTokenService tokens)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _tokens = tokens;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, string? ip, CancellationToken ct)
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
        return await IssueSessionAsync(user, ip, ct);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, string? ip, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _users.GetByEmailAsync(email, ct);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            throw new AuthException("INVALID_CREDENTIALS", "Invalid email or password.");

        return await IssueSessionAsync(user, ip, ct);
    }

    public async Task<AuthResponseDto> RefreshAsync(string? rawToken, string? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new AuthException("REFRESH_INVALID", "Refresh token is required.");

        var hash = TokenHasher.Sha256(rawToken);
        var existing = await _refreshTokens.GetByHashAsync(hash, ct)
            ?? throw new AuthException("REFRESH_INVALID", "Refresh token is invalid.");

        if (existing.RevokedAt is not null)
        {
            await _refreshTokens.RevokeFamilyAsync(existing.UserId, existing.FamilyId, ct);
            throw new AuthException("REFRESH_REUSE", "Refresh token reuse detected. Sign in again.");
        }

        if (existing.ExpiresAt <= DateTime.UtcNow)
            throw new AuthException("REFRESH_INVALID", "Refresh token has expired.");

        var user = await _users.GetByIdAsync(existing.UserId, ct)
            ?? throw new AuthException("REFRESH_INVALID", "User no longer exists.");

        existing.RevokedAt = DateTime.UtcNow;
        await _refreshTokens.UpdateAsync(existing, ct);

        var session = await IssueSessionAsync(user, ip, ct, existing.FamilyId);
        existing.ReplacedByTokenHash = TokenHasher.Sha256(session.RefreshToken!);
        await _refreshTokens.UpdateAsync(existing, ct);
        return session;
    }

    public async Task LogoutAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var existing = await _refreshTokens.GetByHashAsync(TokenHasher.Sha256(rawToken), ct);
        if (existing is null) return;
        await _refreshTokens.RevokeFamilyAsync(existing.UserId, existing.FamilyId, ct);
    }

    public async Task LogoutAllAsync(long userId, CancellationToken ct)
    {
        if (userId <= 0) return;
        await _refreshTokens.RevokeAllForUserAsync(userId, ct);
    }

    private async Task<AuthResponseDto> IssueSessionAsync(AppUser user, string? ip, CancellationToken ct, string? familyId = null)
    {
        var (token, expires) = _tokens.CreateUserToken(user);
        var rawRefresh = TokenHasher.NewOpaqueToken();
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenHasher.Sha256(rawRefresh),
            FamilyId = familyId ?? Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddDays(_tokens.RefreshTokenDays),
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = string.IsNullOrWhiteSpace(ip) ? null : ip.Trim()
        };
        await _refreshTokens.AddAsync(refresh, ct);

        return new AuthResponseDto
        {
            Token = token,
            ExpiresAt = expires,
            RefreshToken = rawRefresh,
            RefreshExpiresAt = refresh.ExpiresAt,
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
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new AuthException("WEAK_PASSWORD", "Password must contain at least one letter and one digit.");
    }
}

public class AuthException : Exception
{
    public string ErrorCode { get; }
    public AuthException(string errorCode, string message) : base(message) => ErrorCode = errorCode;
}
