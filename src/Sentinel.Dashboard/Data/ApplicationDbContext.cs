using Microsoft.EntityFrameworkCore;
using Sentinel.Dashboard.Models.Data;

namespace Sentinel.Dashboard.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Agency> Agencies { get; set; }
        public DbSet<MasterAudio> MasterAudios { get; set; }
        public DbSet<RadioStation> RadioStations { get; set; }
        public DbSet<MatchRecord> MatchRecords { get; set; }
        public DbSet<MonitoringSchedule> MonitoringSchedules { get; set; }
        public DbSet<NotificationLog> NotificationLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<MatchRecord>()
                .HasIndex(m => m.DetectionTime);

            modelBuilder.Entity<MatchRecord>()
                .HasOne(m => m.RadioStation)
                .WithMany(r => r.Matches)
                .HasForeignKey(m => m.RadioStationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MatchRecord>()
                .HasOne(m => m.MasterAudio)
                .WithMany(a => a.Matches)
                .HasForeignKey(m => m.MasterAudioId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MonitoringSchedule>()
                .HasOne(s => s.RadioStation)
                .WithMany(r => r.Schedules)
                .HasForeignKey(s => s.RadioStationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<NotificationLog>()
                .HasOne(n => n.RadioStation)
                .WithMany()
                .HasForeignKey(n => n.RadioStationId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
