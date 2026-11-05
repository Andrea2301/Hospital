using System.ComponentModel.DataAnnotations;

namespace HospitalSV.Models;

public enum AppointmentStatus
{
    Pending,
    Attended,
    Cancelled
}

public class MedicalAppointment : ISoftDeletable, IAuditable
{
    [Key]
    public int Id { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [Required]
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;

    [Required]
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    [Required]
    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    public MedicalReport? MedicalReport { get; set; }

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(150)]
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [MaxLength(150)]
    public string? UpdatedBy { get; set; }
}
