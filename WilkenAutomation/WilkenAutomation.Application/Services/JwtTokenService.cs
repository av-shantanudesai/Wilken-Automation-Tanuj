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
}

public class JwtTokenService
{
    public const string UserIdClaim = "uid";

    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;

    public JwtTokenService(JwtOptions options)
    {
        _options = options;
        if (string.IsNullOrWhiteSpace(options.Key) || options.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be configured and at least 32 characters (set Jwt__Key).");
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));
    }

    public static void EnsureProductionKey(JwtOptions options, string environmentName)
    {
        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase)
            && options.Key.Contains("DevOnly", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to start in Production with the committed development Jwt:Key. Set Jwt__Key to a unique secret.");
        }
    }

    public int AccessTokenMinutes => Math.Clamp(_options.AccessTokenMinutes, 5, 60);
    public int RefreshTokenDays => Math.Clamp(_options.RefreshTokenDays, 1, 30);

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
        return (WriteToken(claims, expires), expires);
    }

    public string CreateWorkerToken()
    {
        var hours = Math.Clamp(_options.WorkerTokenHours, 1, 24);
        var expires = DateTime.UtcNow.AddHours(hours);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "worker"),
            new Claim(ClaimTypes.Role, AuthRoles.Worker)
        };
        return WriteToken(claims, expires);
    }

    private string WriteToken(IEnumerable<Claim> claims, DateTime expires)
    {
        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: expires,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
