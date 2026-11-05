using System.ComponentModel.DataAnnotations;
using HospitalSV.Common;

namespace HospitalSV.DTOs;

// ─── Doctor DTOs ────────────────────────────────────────────────────────────

public record DoctorCreateDto(
    [Required(ErrorMessage = "Doctor name is required.")]
    [MaxLength(150, ErrorMessage = "Name cannot exceed 150 characters.")]
    [RegularExpression(@"^[\p{L}\s\.\'\-]+$", ErrorMessage = "Doctor name contains invalid characters.")]
    string Name,

    [Required(ErrorMessage = "Document number is required.")]
    [MaxLength(20, ErrorMessage = "Document cannot exceed 20 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9\-_]{3,20}$", ErrorMessage = "Document must be between 3 and 20 alphanumeric characters or hyphens.")]
    string Document,

    [Required(ErrorMessage = "Specialty is required.")]
    [MaxLength(100, ErrorMessage = "Specialty cannot exceed 100 characters.")]
    string Specialty,

    [Required(ErrorMessage = "Phone number is required.")]
    [MaxLength(20, ErrorMessage = "Phone cannot exceed 20 characters.")]
    [RegularExpression(@"^\+?[0-9\s\-]{7,20}$", ErrorMessage = "Phone must be a valid number between 7 and 20 digits.")]
    string Phone,

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    [MaxLength(150, ErrorMessage = "Email cannot exceed 150 characters.")]
    string Email
);

public record DoctorUpdateDto(
    [Required(ErrorMessage = "Doctor name is required.")]
    [MaxLength(150, ErrorMessage = "Name cannot exceed 150 characters.")]
    [RegularExpression(@"^[\p{L}\s\.\'\-]+$", ErrorMessage = "Doctor name contains invalid characters.")]
    string Name,

    [Required(ErrorMessage = "Specialty is required.")]
    [MaxLength(100, ErrorMessage = "Specialty cannot exceed 100 characters.")]
    string Specialty,

    [Required(ErrorMessage = "Phone number is required.")]
    [MaxLength(20, ErrorMessage = "Phone cannot exceed 20 characters.")]
    [RegularExpression(@"^\+?[0-9\s\-]{7,20}$", ErrorMessage = "Phone must be a valid number between 7 and 20 digits.")]
    string Phone,

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    [MaxLength(150, ErrorMessage = "Email cannot exceed 150 characters.")]
    string Email
);

public record DoctorResponseDto(
    int    Id,
    string Name,
    string Document,
    string Specialty,
    string Phone,
    string Email
);

// ─── Patient DTOs ───────────────────────────────────────────────────────────

public record PatientCreateDto(
    [Required(ErrorMessage = "Patient full name is required.")]
    [MaxLength(150, ErrorMessage = "Full name cannot exceed 150 characters.")]
    [RegularExpression(@"^[\p{L}\s\.\'\-]+$", ErrorMessage = "Patient name contains invalid characters.")]
    string FullName,

    [Required(ErrorMessage = "Document number is required.")]
    [MaxLength(20, ErrorMessage = "Document cannot exceed 20 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9\-_]{3,20}$", ErrorMessage = "Document must be between 3 and 20 alphanumeric characters or hyphens.")]
    string Document,

    [Required(ErrorMessage = "Phone number is required.")]
    [MaxLength(20, ErrorMessage = "Phone cannot exceed 20 characters.")]
    [RegularExpression(@"^\+?[0-9\s\-]{7,20}$", ErrorMessage = "Phone must be a valid number between 7 and 20 digits.")]
    string Phone,

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    [MaxLength(150, ErrorMessage = "Email cannot exceed 150 characters.")]
    string Email
);

public record PatientUpdateDto(
    [Required(ErrorMessage = "Patient full name is required.")]
    [MaxLength(150, ErrorMessage = "Full name cannot exceed 150 characters.")]
    [RegularExpression(@"^[\p{L}\s\.\'\-]+$", ErrorMessage = "Patient name contains invalid characters.")]
    string FullName,

    [Required(ErrorMessage = "Phone number is required.")]
    [MaxLength(20, ErrorMessage = "Phone cannot exceed 20 characters.")]
    [RegularExpression(@"^\+?[0-9\s\-]{7,20}$", ErrorMessage = "Phone must be a valid number between 7 and 20 digits.")]
    string Phone,

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    [MaxLength(150, ErrorMessage = "Email cannot exceed 150 characters.")]
    string Email
);

public record PatientResponseDto(
    int    Id,
    string FullName,
    string Document,
    string Phone,
    string Email
);

// ─── Medical Appointment DTOs ────────────────────────────────────────────────

public record AppointmentCreateDto(
    [Required(ErrorMessage = "Appointment date and time is required.")]
    DateTime Date,

    [Required(ErrorMessage = "Patient ID is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "Invalid patient ID.")]
    int PatientId,

    [Required(ErrorMessage = "Doctor ID is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "Invalid doctor ID.")]
    int DoctorId
);

public record AppointmentUpdateDto(
    [Required(ErrorMessage = "Appointment date and time is required.")]
    DateTime Date,

    [Required(ErrorMessage = "Patient ID is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "Invalid patient ID.")]
    int PatientId,

    [Required(ErrorMessage = "Doctor ID is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "Invalid doctor ID.")]
    int DoctorId
);

public record AppointmentResponseDto(
    int    Id,
    string Date,
    string Status,
    int    PatientId,
    string PatientName,
    int    DoctorId,
    string DoctorName,
    string DoctorSpecialty
);

// ─── Auth DTOs ───────────────────────────────────────────────────────────────

/// <summary>
/// Public user registration DTO. Notice that 'Role' is intentionally excluded
/// to prevent privilege escalation / JSON mass assignment attacks.
/// </summary>
public record RegisterDto(
    [Required(ErrorMessage = "Full name is required.")]
    [MaxLength(150, ErrorMessage = "Full name cannot exceed 150 characters.")]
    [RegularExpression(@"^[\p{L}\s\.\'\-]+$", ErrorMessage = "Full name contains invalid characters.")]
    string FullName,

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    [MaxLength(150, ErrorMessage = "Email cannot exceed 150 characters.")]
    string Email,

    [Required(ErrorMessage = "Password is required.")]
    [PasswordPolicy]
    string Password
);

public record LoginDto(
    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    string Email,

    [Required(ErrorMessage = "Password is required.")]
    string Password
);

public record AuthResponseDto(
    string Token,
    string FullName,
    string Email,
    string Role,
    DateTime ExpiresAt,
    bool IsDemo = false
);

/// <summary>Role selected in the public sandbox entry cards.</summary>
public record DemoLoginDto(
    [RegularExpression(@"^(Admin|Doctor|Receptionist)$", ErrorMessage = "Role must be 'Admin', 'Doctor', or 'Receptionist'.")]
    string? Role = "Admin"
);

// ─── User Management DTOs (Admin only) ───────────────────────────────────────

public record UserCreateDto(
    [Required(ErrorMessage = "Full name is required.")]
    [MaxLength(150, ErrorMessage = "Full name cannot exceed 150 characters.")]
    [RegularExpression(@"^[\p{L}\s\.\'\-]+$", ErrorMessage = "Full name contains invalid characters.")]
    string FullName,

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    [MaxLength(150, ErrorMessage = "Email cannot exceed 150 characters.")]
    string Email,

    [Required(ErrorMessage = "Password is required.")]
    [PasswordPolicy]
    string Password,

    [Required(ErrorMessage = "Role is required.")]
    [RegularExpression(@"^(Admin|Doctor|Receptionist)$", ErrorMessage = "Role must be 'Admin', 'Doctor', or 'Receptionist'.")]
    string Role
);

public record UserUpdateRoleDto(
    [Required(ErrorMessage = "Role is required.")]
    [RegularExpression(@"^(Admin|Doctor|Receptionist)$", ErrorMessage = "Role must be 'Admin', 'Doctor', or 'Receptionist'.")]
    string Role
);

public record UserResponseDto(
    int      Id,
    string   FullName,
    string   Email,
    string   Role,
    bool     IsActive,
    DateTime CreatedAt,
    string?  CreatedBy
);

// ─── Medical Report DTOs (Clinical Encounters) ───────────────────────────────

public record MedicalReportCreateDto(
    [Required(ErrorMessage = "Appointment ID is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "Invalid appointment ID.")]
    int AppointmentId,

    [Required(ErrorMessage = "Reason for visit / symptoms is required.")]
    [MaxLength(500, ErrorMessage = "Reason for visit cannot exceed 500 characters.")]
    string ReasonForVisit,

    [MaxLength(1000, ErrorMessage = "Physical examination cannot exceed 1000 characters.")]
    string? PhysicalExamination,

    [MaxLength(20, ErrorMessage = "Blood pressure format cannot exceed 20 characters.")]
    [RegularExpression(@"^([0-9]{2,3}/[0-9]{2,3}(\s*mmHg)?)?$", ErrorMessage = "Blood pressure must follow format: '120/80' or '120/80 mmHg'.")]
    string? BloodPressure,

    [Range(30, 250, ErrorMessage = "Heart rate must be between 30 and 250 bpm.")]
    int? HeartRate,

    [Range(30.0, 45.0, ErrorMessage = "Temperature must be between 30.0 and 45.0 °C.")]
    decimal? Temperature,

    [Range(0.5, 500.0, ErrorMessage = "Weight must be between 0.5 and 500.0 kg.")]
    decimal? WeightKg,

    [Range(20.0, 250.0, ErrorMessage = "Height must be between 20.0 and 250.0 cm.")]
    decimal? HeightCm,

    [Required(ErrorMessage = "Clinical diagnosis is required.")]
    [MaxLength(1000, ErrorMessage = "Diagnosis cannot exceed 1000 characters.")]
    string Diagnosis,

    [Required(ErrorMessage = "Prescription and treatment plan is required.")]
    [MaxLength(3000, ErrorMessage = "Treatment plan cannot exceed 3000 characters.")]
    string Treatment,

    [MaxLength(1500, ErrorMessage = "Clinical notes cannot exceed 1500 characters.")]
    string? Notes,

    bool IsFinalized = true
);

public record MedicalReportUpdateDto(
    [Required(ErrorMessage = "Reason for visit / symptoms is required.")]
    [MaxLength(500, ErrorMessage = "Reason for visit cannot exceed 500 characters.")]
    string ReasonForVisit,

    [MaxLength(1000, ErrorMessage = "Physical examination cannot exceed 1000 characters.")]
    string? PhysicalExamination,

    [MaxLength(20, ErrorMessage = "Blood pressure format cannot exceed 20 characters.")]
    [RegularExpression(@"^([0-9]{2,3}/[0-9]{2,3}(\s*mmHg)?)?$", ErrorMessage = "Blood pressure must follow format: '120/80' or '120/80 mmHg'.")]
    string? BloodPressure,

    [Range(30, 250, ErrorMessage = "Heart rate must be between 30 and 250 bpm.")]
    int? HeartRate,

    [Range(30.0, 45.0, ErrorMessage = "Temperature must be between 30.0 and 45.0 °C.")]
    decimal? Temperature,

    [Range(0.5, 500.0, ErrorMessage = "Weight must be between 0.5 and 500.0 kg.")]
    decimal? WeightKg,

    [Range(20.0, 250.0, ErrorMessage = "Height must be between 20.0 and 250.0 cm.")]
    decimal? HeightCm,

    [Required(ErrorMessage = "Clinical diagnosis is required.")]
    [MaxLength(1000, ErrorMessage = "Diagnosis cannot exceed 1000 characters.")]
    string Diagnosis,

    [Required(ErrorMessage = "Prescription and treatment plan is required.")]
    [MaxLength(3000, ErrorMessage = "Treatment plan cannot exceed 3000 characters.")]
    string Treatment,

    [MaxLength(1500, ErrorMessage = "Clinical notes cannot exceed 1500 characters.")]
    string? Notes,

    bool IsFinalized = true
);

public record MedicalReportResponseDto(
    int      Id,
    int      AppointmentId,
    string   AppointmentDate,
    int      PatientId,
    string   PatientName,
    string   PatientDocument,
    int      DoctorId,
    string   DoctorName,
    string   DoctorSpecialty,
    string   ReasonForVisit,
    string?  PhysicalExamination,
    string?  BloodPressure,
    int?     HeartRate,
    decimal? Temperature,
    decimal? WeightKg,
    decimal? HeightCm,
    decimal? Bmi,
    string   Diagnosis,
    string   Treatment,
    string?  Notes,
    bool     IsFinalized,
    DateTime CreatedAt,
    string?  CreatedBy
);
