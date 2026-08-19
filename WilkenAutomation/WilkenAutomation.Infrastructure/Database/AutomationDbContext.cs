using Microsoft.EntityFrameworkCore;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Infrastructure.Database;

public class AutomationDbContext : DbContext
{
    public AutomationDbContext(DbContextOptions<AutomationDbContext> options) : base(options) { }

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AutomationRun> AutomationRuns => Set<AutomationRun>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
    public DbSet<JobAttempt> JobAttempts => Set<JobAttempt>();
    public DbSet<AutomationLog> AutomationLogs => Set<AutomationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.ToTable("AppUsers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.DisplayName).HasMaxLength(128);
            e.Property(x => x.PasswordHash).HasMaxLength(256);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.FamilyId });
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.FamilyId).HasMaxLength(32);
            e.Property(x => x.CreatedByIp).HasMaxLength(64);
            e.Ignore(x => x.IsActive);
        });

        modelBuilder.Entity<AutomationRun>(e =>
        {
            e.ToTable("AutomationRuns");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RunId).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.UpdatedAt);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(1000);
        });

        modelBuilder.Entity<ExportJob>(e =>
        {
            e.ToTable("ExportJobs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.JobId).IsUnique();
            e.HasIndex(x => new { x.RunId, x.Client, x.FiscalYear, x.Department }).IsUnique();
            e.HasIndex(x => new { x.RunId, x.Status, x.OrderIndex });
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Client);
            e.HasIndex(x => x.FiscalYear);
            e.HasIndex(x => x.UpdatedAt);
            e.Property(x => x.JobId).HasMaxLength(96);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.Client).HasMaxLength(32);
            e.Property(x => x.Department).HasMaxLength(64);
            e.Property(x => x.DepartmentCode).HasMaxLength(8);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ValidationStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ApplicationState).HasMaxLength(40);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.ErrorCode).HasMaxLength(64);
            e.Property(x => x.ErrorMessage).HasMaxLength(1024);
            e.Property(x => x.ValidationDetail).HasMaxLength(1024);
        });

        modelBuilder.Entity<JobAttempt>(e =>
        {
            e.ToTable("JobAttempts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.JobId, x.AttemptNumber }).IsUnique();
            e.Property(x => x.JobId).HasMaxLength(96);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ErrorCode).HasMaxLength(64);
            e.Property(x => x.ErrorMessage).HasMaxLength(1024);
        });

        modelBuilder.Entity<AutomationLog>(e =>
        {
            e.ToTable("AutomationLogs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => x.JobId);
            e.HasIndex(x => x.Timestamp);
            e.Property(x => x.RunId).HasMaxLength(64);
            e.Property(x => x.JobId).HasMaxLength(96);
            e.Property(x => x.Client).HasMaxLength(32);
            e.Property(x => x.Department).HasMaxLength(64);
            e.Property(x => x.Level).HasMaxLength(8);
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.Message).HasMaxLength(2048);
            e.Property(x => x.ApplicationState).HasMaxLength(40);
            e.Property(x => x.ErrorCode).HasMaxLength(64);
        });
    }
}
