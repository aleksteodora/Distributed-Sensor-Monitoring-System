using Microsoft.EntityFrameworkCore;
using Shared.Models;

namespace Shared.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Sensor> Sensors => Set<Sensor>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<ConsensusValue> ConsensusValues => Set<ConsensusValue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Sensor>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasMaxLength(64);
            e.Property(s => s.Quality).HasConversion<string>();
            e.Property(s => s.Status).HasConversion<string>();
        });

        modelBuilder.Entity<Measurement>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.SensorId);
            e.HasIndex(m => m.MeasuredAt);
            e.HasIndex(m => m.IsConsensus);
            e.Property(m => m.Quality).HasConversion<string>();
            e.Property(m => m.AlarmPriority).HasConversion<string>();
            e.HasOne(m => m.Sensor)
             .WithMany(s => s.Measurements)
             .HasForeignKey(m => m.SensorId);
        });

        modelBuilder.Entity<ConsensusValue>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.PeriodStart);
        });
    }
}
