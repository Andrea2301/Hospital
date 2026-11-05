namespace HospitalSV.Services;

/// <summary>
/// SMTP configuration for outbound transactional emails.
/// Bound from the "Email" section of appsettings.json.
/// </summary>
public class EmailSettings
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "HospitalSV";

    public string HospitalName { get; set; } = "HospitalSV";
    public string SupportEmail { get; set; } = string.Empty;
    public string SupportPhone { get; set; } = string.Empty;

    /// <summary>Duration used in the attached calendar invitation.</summary>
    public int AppointmentDurationMinutes { get; set; } = 30;

    /// <summary>
    /// Offset in minutes applied when showing appointment times in the email
    /// (e.g. -360 for El Salvador, UTC-06:00). Calendar invites always use UTC.
    /// </summary>
    public int TimeZoneOffsetMinutes { get; set; } = -360;

    /// <summary>Attach an .ics file so the patient can add the appointment to their calendar.</summary>
    public bool AttachCalendarInvite { get; set; } = true;

    public bool NotifyOnCreate { get; set; } = true;
    public bool NotifyOnReschedule { get; set; } = true;
    public bool NotifyOnCancel { get; set; } = true;
}
