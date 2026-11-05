using HospitalSV.Models;

namespace HospitalSV.Services;

public interface IEmailService
{
    /// <summary>
    /// Sends an appointment confirmation/reschedule email to the patient.
    /// Returns false when email is disabled, not configured, or SMTP fails.
    /// </summary>
    Task<bool> SendAppointmentConfirmationAsync(
        MedicalAppointment appointment,
        bool isReschedule = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an appointment cancellation notice to the patient.
    /// Returns false when email is disabled, not configured, or SMTP fails.
    /// </summary>
    Task<bool> SendAppointmentCancellationAsync(
        MedicalAppointment appointment,
        CancellationToken cancellationToken = default);
}
