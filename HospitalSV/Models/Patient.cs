using System.ComponentModel.DataAnnotations;

namespace HospitalSV.Models;

public class Patient : ISoftDeletable, IAuditable
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Document { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(150)]
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [MaxLength(150)]
    public string? UpdatedBy { get; set; }

    public ICollection<MedicalAppointment> MedicalAppointments { get; set; } = new List<MedicalAppointment>();
}