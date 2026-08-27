using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Tests;

/// <summary>
/// Proves the strict key/audience binding used by the API bearer scheme:
/// a token signed with the user key can never validate as a worker token
/// (and vice versa), even though both keys are trusted by the same scheme.
/// </summary>
public class JwtAudienceSeparationTests
{
    private const string UserKey = "WilkenAutomation-DevOnly-ChangeThisKey-32ch";
    private const string WorkerKey = "WilkenAutomation-DevOnly-WorkerKey-32chars!";

    private readonly JwtTokenService _tokens = new(new JwtOptions
    {
        Key = UserKey,
        WorkerKey = WorkerKey
    });

    /// <summary>Mirrors the TokenValidationParameters configured in the API's Program.cs.</summary>
    private TokenValidationParameters ApiValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        ValidIssuer = _tokens.Issuer,
        ValidAudiences = _tokens.ValidAudiences,
        IssuerSigningKeyResolver = (_, securityToken, _, _) => _tokens.ResolveSigningKeys(securityToken),
        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.Name,
    };

    private static ClaimsPrincipal Validate(string token, TokenValidationParameters parameters) =>
        new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);

    [Fact]
    public void UserToken_Validates_AndCarriesUserRole()
    {
        var (token, _) = _tokens.CreateUserToken(new AppUser
        {
            Id = 7,
            Email = "user@example.com",
            DisplayName = "User"
        });

        var principal = Validate(token, ApiValidationParameters());

        Assert.True(principal.IsInRole(AuthRoles.User));
        Assert.False(principal.IsInRole(AuthRoles.Worker));
    }

    [Fact]
    public void WorkerToken_Validates_AndCarriesWorkerRole()
    {
        var principal = Validate(_tokens.CreateWorkerToken(), ApiValidationParameters());

        Assert.True(principal.IsInRole(AuthRoles.Worker));
        Assert.False(principal.IsInRole(AuthRoles.User));
    }

    [Fact]
    public void UserKeySignedToken_ClaimingWorkerAudience_IsRejected()
    {
        var forged = ForgeToken(UserKey, _tokens.WorkerAudience, AuthRoles.Worker);

        Assert.ThrowsAny<SecurityTokenException>(() => Validate(forged, ApiValidationParameters()));
    }

    [Fact]
    public void WorkerKeySignedToken_ClaimingUserAudience_IsRejected()
    {
        var forged = ForgeToken(WorkerKey, _tokens.UserAudience, AuthRoles.User);

        Assert.ThrowsAny<SecurityTokenException>(() => Validate(forged, ApiValidationParameters()));
    }

    [Fact]
    public void Token_WithUnknownAudience_IsRejected()
    {
        var forged = ForgeToken(UserKey, "SomeOtherAudience", AuthRoles.User);

        Assert.ThrowsAny<SecurityTokenException>(() => Validate(forged, ApiValidationParameters()));
    }

    [Fact]
    public void ResolveSigningKeys_ReturnsNoKeys_ForMultiAudienceToken()
    {
        var handler = new JwtSecurityTokenHandler();
        var multi = new JwtSecurityToken(
            issuer: _tokens.Issuer,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Aud, _tokens.UserAudience),
                new Claim(JwtRegisteredClaimNames.Aud, _tokens.WorkerAudience)
            },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(UserKey)), SecurityAlgorithms.HmacSha256));

        Assert.Empty(_tokens.ResolveSigningKeys(handler.ReadJwtToken(handler.WriteToken(multi))));
    }

    private string ForgeToken(string signingKey, string audience, string role)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _tokens.Issuer,
            audience: audience,
            claims: new[] { new Claim(ClaimTypes.Role, role) },
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
