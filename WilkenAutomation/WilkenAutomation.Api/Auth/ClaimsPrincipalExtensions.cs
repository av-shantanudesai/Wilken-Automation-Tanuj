using System.Security.Claims;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static long GetUserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(JwtTokenService.UserIdClaim)
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub");
        return long.TryParse(raw, out var id) ? id : 0;
    }
}
