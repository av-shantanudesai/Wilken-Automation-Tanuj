using System.Security.Cryptography;
using WilkenAutomation.Application.Interfaces;

namespace WilkenAutomation.Infrastructure.FileSystem;

public class ChecksumService : IChecksumService
{
    public async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
