using Microsoft.EntityFrameworkCore;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Infrastructure.Database;

namespace WilkenAutomation.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AutomationDbContext _db;

    public RefreshTokenRepository(AutomationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync(ct);
    }

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct) =>
        _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task UpdateAsync(RefreshToken token, CancellationToken ct)
    {
        _db.RefreshTokens.Update(token);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeFamilyAsync(long userId, string familyId, CancellationToken ct)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var token in tokens)
            token.RevokedAt = now;
        if (tokens.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(long userId, CancellationToken ct)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var token in tokens)
            token.RevokedAt = now;
        if (tokens.Count > 0)
            await _db.SaveChangesAsync(ct);
    }
}
