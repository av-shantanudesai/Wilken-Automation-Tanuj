using Microsoft.EntityFrameworkCore;
using WilkenExport.Api.Domain;

namespace WilkenExport.Api.Data;

public class ExportDbContext : DbContext
{
    public ExportDbContext(DbContextOptions<ExportDbContext> options) : base(options) { }

    public DbSet<ExportRun> Runs => Set<ExportRun>();
    public DbSet<ExportJob> Jobs => Set<ExportJob>();
    public DbSet<JobLogEntry> Logs => Set<JobLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExportRun>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasMaxLength(64);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(r => r.ConfigJson).HasColumnType("TEXT");
            e.HasMany(r => r.Jobs).WithOne(j => j.Run).HasForeignKey(j => j.RunId);
        });

        modelBuilder.Entity<ExportJob>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.Id).HasMaxLength(128);
            e.Property(j => j.RunId).HasMaxLength(64);
            e.Property(j => j.Client).HasMaxLength(16);
            e.Property(j => j.Department).HasMaxLength(64);
            e.Property(j => j.DepartmentCode).HasMaxLength(8);
            e.Property(j => j.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(j => j.ValidationOutcome).HasConversion<string>().HasMaxLength(32);
            e.Property(j => j.FileName).HasMaxLength(256);
            e.Property(j => j.FilePath).HasMaxLength(1024);
            e.Property(j => j.Sha256).HasMaxLength(64);
            e.Property(j => j.ErrorCode).HasMaxLength(64);
            e.HasIndex(j => new { j.RunId, j.Status });
            e.HasIndex(j => new { j.RunId, j.OrderIndex });
        });

        modelBuilder.Entity<JobLogEntry>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.Property(l => l.RunId).HasMaxLength(64);
            e.Property(l => l.JobId).HasMaxLength(128);
            e.Property(l => l.Level).HasMaxLength(8);
            e.Property(l => l.Action).HasMaxLength(64);
            e.Property(l => l.ErrorCode).HasMaxLength(64);
            e.HasIndex(l => new { l.RunId, l.Timestamp });
            e.HasIndex(l => l.JobId);
        });
    }
}
