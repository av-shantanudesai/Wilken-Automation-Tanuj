using Microsoft.EntityFrameworkCore;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Infrastructure.Database;

namespace WilkenAutomation.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AutomationDbContext _db;

    public UserRepository(AutomationDbContext db)
    {
        _db = db;
    }

    public Task<AppUser?> GetByIdAsync(long id, CancellationToken ct) =>
        _db.AppUsers.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct) =>
        _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<AppUser?> GetFirstAsync(CancellationToken ct) =>
        _db.AppUsers.OrderBy(u => u.Id).FirstOrDefaultAsync(ct);

    public async Task<AppUser> CreateAsync(AppUser user, CancellationToken ct)
    {
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }
}
