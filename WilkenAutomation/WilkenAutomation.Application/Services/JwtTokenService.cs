using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

public static class AuthRoles
{
    public const string User = "User";
    public const string Worker = "Worker";
}

public static class HubGroups
{
    public const string Workers = "workers";
    public static string User(long userId) => $"user:{userId}";
    public static string Run(string runId) => $"run:{runId}";
}

public class JwtTokenService
{
    public const string UserIdClaim = "uid";

    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _userKey;
    private readonly SymmetricSecurityKey _workerKey;

    public JwtTokenService(JwtOptions options)
    {
        _options = options;
        if (string.IsNullOrWhiteSpace(options.Key) || options.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be configured and at least 32 characters (set Jwt__Key).");
        _userKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));

        var workerSecret = string.IsNullOrWhiteSpace(options.WorkerKey) ? options.Key : options.WorkerKey;
        if (workerSecret.Length < 32)
            throw new InvalidOperationException("Jwt:WorkerKey must be at least 32 characters (set Jwt__WorkerKey).");
        _workerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(workerSecret));
    }

    public static void EnsureProductionKey(JwtOptions options, string environmentName)
    {
        if (!string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
            return;

        if (options.Key.Contains("DevOnly", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to start in Production with the committed development Jwt:Key. Set Jwt__Key to a unique secret.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkerKey)
            || options.WorkerKey.Contains("DevOnly", StringComparison.OrdinalIgnoreCase)
            || options.WorkerKey == options.Key)
        {
            throw new InvalidOperationException(
                "Refusing to start in Production without a distinct Jwt:WorkerKey. Set Jwt__WorkerKey.");
        }
    }

    public int AccessTokenMinutes => Math.Clamp(_options.AccessTokenMinutes, 5, 60);
    public int RefreshTokenDays => Math.Clamp(_options.RefreshTokenDays, 1, 30);
    public int WorkerTokenHours => Math.Clamp(_options.WorkerTokenHours, 1, 4);
    public string UserAudience => _options.Audience;
    public string WorkerAudience =>
        string.IsNullOrWhiteSpace(_options.WorkerAudience) ? "WilkenAutomation.Worker" : _options.WorkerAudience;
    public string Issuer => _options.Issuer;
    public IEnumerable<string> ValidAudiences => new[] { UserAudience, WorkerAudience };

    /// <summary>
    /// Strict key/audience binding: a token is only checked against the key that
    /// matches its single claimed audience. A token signed with the user key can
    /// therefore never validate as a worker token (and vice versa), even though
    /// both keys are trusted by the same bearer scheme.
    /// </summary>
    public IEnumerable<SecurityKey> ResolveSigningKeys(SecurityToken token)
    {
        var audiences = token switch
        {
            Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt => jwt.Audiences.ToList(),
            JwtSecurityToken jwt => jwt.Audiences.ToList(),
            _ => new List<string>()
        };

        if (audiences.Count != 1)
            return Array.Empty<SecurityKey>();
        if (string.Equals(audiences[0], WorkerAudience, StringComparison.Ordinal))
            return new SecurityKey[] { _workerKey };
        if (string.Equals(audiences[0], UserAudience, StringComparison.Ordinal))
            return new SecurityKey[] { _userKey };
        return Array.Empty<SecurityKey>();
    }

    public (string Token, DateTime ExpiresAt) CreateUserToken(AppUser user)
    {
        var expires = DateTime.UtcNow.AddMinutes(AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(UserIdClaim, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, AuthRoles.User)
        };
        return (WriteToken(claims, expires, _userKey, UserAudience), expires);
    }

    public string CreateWorkerToken()
    {
        var expires = DateTime.UtcNow.AddHours(WorkerTokenHours);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "worker"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(ClaimTypes.Role, AuthRoles.Worker)
        };
        return WriteToken(claims, expires, _workerKey, WorkerAudience);
    }

    private string WriteToken(IEnumerable<Claim> claims, DateTime expires, SymmetricSecurityKey key, string audience)
    {
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: expires,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
