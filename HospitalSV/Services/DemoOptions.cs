namespace HospitalSV.Services;

/// <summary>
/// Configuration for the public sandbox. When enabled, the API seeds a
/// fictional clinic, exposes a password-less demo login, blocks public
/// registration and email delivery, and resets the data on a schedule.
/// Bind from the "Demo" section of configuration.
/// </summary>
public class DemoOptions
{
    public const string SectionName = "Demo";

    public bool Enabled { get; set; }

    /// <summary>Hours between automatic resets. Clamped to at least 1.</summary>
    public int ResetIntervalHours { get; set; } = 24;

    /// <summary>Minimum minutes between two manual resets from the UI.</summary>
    public int ManualResetCooldownMinutes { get; set; } = 10;

    /// <summary>Account used by the "Administrator" demo card.</summary>
    public string AdminEmail { get; set; } = "admin@hospital.com";

    /// <summary>Account used by the "Doctor" demo card. It must match a seeded doctor email.</summary>
    public string DoctorEmail { get; set; } = "doctor.laura@hospital.com";

    /// <summary>Account used by the "Receptionist" demo card.</summary>
    public string ReceptionistEmail { get; set; } = "reception@hospital.com";
}
