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

public class JwtTokenService
{
    public const string UserIdClaim = "uid";

    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;

    public JwtTokenService(JwtOptions options)
    {
        _options = options;
        if (string.IsNullOrWhiteSpace(options.Key) || options.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be configured and at least 32 characters.");
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));
    }

    public (string Token, DateTime ExpiresAt) CreateUserToken(AppUser user)
    {
        var expires = DateTime.UtcNow.AddMinutes(Math.Max(15, _options.AccessTokenMinutes));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(UserIdClaim, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, AuthRoles.User)
        };
        return (WriteToken(claims, expires), expires);
    }

    public string CreateWorkerToken()
    {
        var expires = DateTime.UtcNow.AddDays(7);
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
            expires: expires,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
