using System.Security.Claims;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Dashboard user id. Throws if the principal is not a signed-in user.</summary>
    public static long GetRequiredUserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(JwtTokenService.UserIdClaim)
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub");
        if (!long.TryParse(raw, out var id) || id <= 0)
            throw new UnauthorizedAccessException("Authenticated user identity is missing or invalid.");
        return id;
    }
}
