using System.Security.Cryptography;
using System.Text;

namespace WilkenAutomation.Application.Services;

/// <summary>SHA-256 for high-entropy refresh tokens (PBKDF2 is unnecessary here).</summary>
public static class TokenHasher
{
    public static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    public static string NewOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }
}
