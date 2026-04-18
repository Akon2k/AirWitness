using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sentinel.Dashboard.Models.Data
{
    public class Agency
    {
        public int Id { get; set; }
        
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Relación con los audios maestros de esta agencia
        public ICollection<MasterAudio> MasterAudios { get; set; } = new List<MasterAudio>();
    }

    public class MasterAudio
    {
        public int Id { get; set; }
        
        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;
        
        public string? LocalPath { get; set; }
        public double Duration { get; set; }
        
        public int? AgencyId { get; set; }
        public Agency? Agency { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Relación con las detecciones
        public ICollection<MatchRecord> Matches { get; set; } = new List<MatchRecord>();
    }

    public class RadioStation
    {
        public int Id { get; set; }
        
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        public string StreamUrl { get; set; } = string.Empty;

        public string? City { get; set; }
        public string? Region { get; set; }
        public string? Frequency { get; set; }
        public string? Category { get; set; }
        public string? DefaultMasterPath { get; set; }
        
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Relación con las detecciones
        public ICollection<MatchRecord> Matches { get; set; } = new List<MatchRecord>();
        
        // Relación con los horarios programados
        public ICollection<MonitoringSchedule> Schedules { get; set; } = new List<MonitoringSchedule>();
    }

    public class MonitoringSchedule
    {
        public int Id { get; set; }
        
        public int RadioStationId { get; set; }
        public RadioStation? RadioStation { get; set; }
        
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        
        // Almacenamos los días como string (ej: "1,2,3,4,5") para flexibilidad futura
        public string? DaysOfWeek { get; set; } 
        
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class MatchRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public DateTime DetectionTime { get; set; } = DateTime.UtcNow;
        
        public int RadioStationId { get; set; }
        public RadioStation? RadioStation { get; set; }
        
        public int MasterAudioId { get; set; }
        public MasterAudio? MasterAudio { get; set; }
        
        public double Confidence { get; set; }
        public double MatchOffsetSeconds { get; set; }
        public double StreamElapsedSeconds { get; set; }
        
        public string? EvidenceFileName { get; set; }
        
        [NotMapped]
        public string? EvidenceUrl => !string.IsNullOrEmpty(EvidenceFileName) ? $"/evidence/{EvidenceFileName}" : null;
    }
}
