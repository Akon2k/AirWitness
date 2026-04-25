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

            // Configuraciones adicionales si fuera necesario
            modelBuilder.Entity<MatchRecord>()
                .HasIndex(m => m.DetectionTime);

            modelBuilder.Entity<MatchRecord>()
                .HasOne(m => m.RadioStation)
                .WithMany(r => r.Matches)
                .HasForeignKey(m => m.RadioStationId);

            modelBuilder.Entity<MatchRecord>()
                .HasOne(m => m.MasterAudio)
                .WithMany(a => a.Matches)
                .HasForeignKey(m => m.MasterAudioId);

            modelBuilder.Entity<MonitoringSchedule>()
                .HasOne(s => s.RadioStation)
                .WithMany(r => r.Schedules)
                .HasForeignKey(s => s.RadioStationId);
        }
    }
}
