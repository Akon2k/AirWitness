using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sentinel.Dashboard.Data;
using Sentinel.Dashboard.Models.Data;

namespace Sentinel.Dashboard.Services
{
    public interface IAlertService
    {
        Task NotifyDetectionAsync(RadioStation station, MasterAudio audio, double confidence);
        Task NotifyStreamFailureAsync(RadioStation station, string errorMessage);
        Task<List<NotificationLog>> GetRecentNotificationsAsync(int count = 20);
        Task MarkAsReadAsync(int notificationId);
    }

    public class AlertService : IAlertService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly ILogger<AlertService> _logger;

        public AlertService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<AlertService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public async Task NotifyDetectionAsync(RadioStation station, MasterAudio audio, double confidence)
        {
            var title = "🚨 Comercial Detectado";
            var message = $"Se ha detectado el comercial '{audio.Title}' en la emisora '{station.Name}' con una confianza del {confidence:P1}.";

            _logger.LogInformation("[ALERT] {Title}: {Message}", title, message);
            
            // Simulación de envío de Email/Push
            _logger.LogInformation("[PUSH] Enviando notificación a dispositivos vinculados...");
            _logger.LogInformation("[EMAIL] Enviando reporte detallado a auditoría@airwitness.pro");

            await SaveNotificationAsync(NotificationType.Success, title, message, station.Id);
        }

        public async Task NotifyStreamFailureAsync(RadioStation station, string errorMessage)
        {
            var title = "⚠️ Falla de Stream";
            var message = $"La emisora '{station.Name}' ha perdido la conexión. Error: {errorMessage}";

            _logger.LogWarning("[ALERT] {Title}: {Message}", title, message);
            
            // Alerta crítica (roja) para el celular
            _logger.LogWarning("[PUSH-CRITICAL] ¡Atención! Radio offline: {Name}", station.Name);

            await SaveNotificationAsync(NotificationType.Error, title, message, station.Id);
        }

        public async Task<List<NotificationLog>> GetRecentNotificationsAsync(int count = 20)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            return await db.NotificationLogs
                .Include(n => n.RadioStation)
                .OrderByDescending(n => n.Timestamp)
                .Take(count)
                .ToListAsync();
        }

        public async Task MarkAsReadAsync(int notificationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var notification = await db.NotificationLogs.FindAsync(notificationId);
            if (notification != null)
            {
                notification.IsRead = true;
                await db.SaveChangesAsync();
            }
        }

        private async Task SaveNotificationAsync(NotificationType type, string title, string message, int? stationId)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                var log = new NotificationLog
                {
                    Type = type,
                    Title = title,
                    Message = message,
                    RadioStationId = stationId,
                    Timestamp = DateTime.UtcNow
                };

                db.NotificationLogs.Add(log);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar la notificación en la base de datos.");
            }
        }
    }
}
