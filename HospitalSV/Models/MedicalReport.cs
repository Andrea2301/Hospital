using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HospitalSV.Models;

public class MedicalReport : IAuditable, ISoftDeletable
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int AppointmentId { get; set; }

    [ForeignKey(nameof(AppointmentId))]
    public MedicalAppointment? Appointment { get; set; }

    [Required]
    public int PatientId { get; set; }

    [ForeignKey(nameof(PatientId))]
    public Patient? Patient { get; set; }

    [Required]
    public int DoctorId { get; set; }

    [ForeignKey(nameof(DoctorId))]
    public Doctor? Doctor { get; set; }

    [Required]
    [MaxLength(500)]
    public string ReasonForVisit { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? PhysicalExamination { get; set; }

    // Vital signs
    [MaxLength(20)]
    public string? BloodPressure { get; set; } // e.g. "120/80 mmHg"

    public int? HeartRate { get; set; } // e.g. 75 bpm

    [Column(TypeName = "decimal(4,1)")]
    public decimal? Temperature { get; set; } // e.g. 36.5 °C

    [Column(TypeName = "decimal(5,2)")]
    public decimal? WeightKg { get; set; } // e.g. 72.50 kg

    [Column(TypeName = "decimal(5,2)")]
    public decimal? HeightCm { get; set; } // e.g. 175.00 cm

    // Clinical Diagnosis and Prescription
    [Required]
    [MaxLength(1000)]
    public string Diagnosis { get; set; } = string.Empty;

    [Required]
    [MaxLength(3000)]
    public string Treatment { get; set; } = string.Empty;

    [MaxLength(1500)]
    public string? Notes { get; set; }

    public bool IsFinalized { get; set; } = true;

    // Audit fields (IAuditable)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(150)]
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [MaxLength(150)]
    public string? UpdatedBy { get; set; }

    // Soft delete (ISoftDeletable)
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
