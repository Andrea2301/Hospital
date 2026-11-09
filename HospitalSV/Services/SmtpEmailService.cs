using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using HospitalSV.Models;
using Microsoft.Extensions.Options;

namespace HospitalSV.Services;

/// <summary>
/// Sends polished transactional emails through a configurable SMTP server.
/// Email failures are logged and never bubble up to the API caller.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private enum AppointmentEmailKind
    {
        Confirmation,
        Reschedule,
        Cancellation
    }

    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailSettings> settings, ILogger<SmtpEmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<bool> SendAppointmentConfirmationAsync(
        MedicalAppointment appointment,
        bool isReschedule = false,
        CancellationToken cancellationToken = default)
        => SendAppointmentEmailAsync(
            appointment,
            isReschedule ? AppointmentEmailKind.Reschedule : AppointmentEmailKind.Confirmation,
            cancellationToken);

    public Task<bool> SendAppointmentCancellationAsync(
        MedicalAppointment appointment,
        CancellationToken cancellationToken = default)
        => SendAppointmentEmailAsync(appointment, AppointmentEmailKind.Cancellation, cancellationToken);

    private async Task<bool> SendAppointmentEmailAsync(
        MedicalAppointment appointment,
        AppointmentEmailKind kind,
        CancellationToken cancellationToken)
    {
        if (!ShouldSend(kind))
            return false;

        var patient = appointment.Patient;
        var doctor = appointment.Doctor;

        if (patient is null || string.IsNullOrWhiteSpace(patient.Email) || doctor is null)
        {
            _logger.LogWarning(
                "Appointment #{AppointmentId} email skipped: missing patient email or doctor data.",
                appointment.Id);
            return false;
        }

        try
        {
            using var message = BuildMessage(appointment, patient, doctor, kind);
            using var client = BuildClient();

            await client.SendMailAsync(message, cancellationToken);

            _logger.LogInformation(
                "Appointment #{AppointmentId} {EmailKind} email sent to {Recipient} (intended recipient: {IntendedRecipient}).",
                appointment.Id,
                kind,
                GetEffectiveRecipient(patient.Email),
                patient.Email);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not send appointment #{AppointmentId} {EmailKind} email to {Recipient}.",
                appointment.Id,
                kind,
                patient.Email);

            return false;
        }
    }

    private bool ShouldSend(AppointmentEmailKind kind)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Email delivery is disabled; appointment email was skipped.");
            return false;
        }

        var enabledForKind = kind switch
        {
            AppointmentEmailKind.Confirmation => _settings.NotifyOnCreate,
            AppointmentEmailKind.Reschedule   => _settings.NotifyOnReschedule,
            AppointmentEmailKind.Cancellation => _settings.NotifyOnCancel,
            _                                 => false
        };

        if (!enabledForKind)
            return false;

        if (string.IsNullOrWhiteSpace(_settings.Host) || string.IsNullOrWhiteSpace(_settings.FromEmail))
        {
            _logger.LogWarning(
                "Email settings are incomplete (Host/FromEmail); appointment email was skipped.");
            return false;
        }

        return true;
    }

    private MailMessage BuildMessage(
        MedicalAppointment appointment,
        Patient patient,
        Doctor doctor,
        AppointmentEmailKind kind)
    {
        var dateUtc = ToUtc(appointment.Date);
        var dateText = FormatDisplayDate(dateUtc);

        var (title, badgeLabel, badgeBackground, badgeText, intro, note) = GetContent(kind);
        var html = BuildHtml(appointment, patient, doctor, kind, dateText, title, badgeLabel, badgeBackground, badgeText, intro, note);
        var plainText = BuildPlainText(appointment, patient, doctor, dateText, title, intro, note);

        var subject = $"{title} - #{appointment.Id}";
        var intendedRecipient = patient.Email;
        var effectiveRecipient = GetEffectiveRecipient(intendedRecipient);
        var isRedirected = !string.Equals(effectiveRecipient, intendedRecipient, StringComparison.OrdinalIgnoreCase);

        var message = new MailMessage
        {
            From = new MailAddress(_settings.FromEmail, _settings.FromName),
            Subject = isRedirected
                ? $"[Demo -> {intendedRecipient}] {subject}"
                : subject,
            SubjectEncoding = Encoding.UTF8,
            Body = plainText,
            IsBodyHtml = false
        };

        message.To.Add(effectiveRecipient);

        if (isRedirected)
        {
            message.Headers.Add("X-Original-Recipient", intendedRecipient);
        }

        message.AlternateViews.Add(
            AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html"));

        if (_settings.AttachCalendarInvite)
        {
            message.Attachments.Add(BuildCalendarAttachment(appointment, doctor, kind));
        }

        return message;
    }

    private static (string Title, string BadgeLabel, string BadgeBackground, string BadgeText, string Intro, string Note)
        GetContent(AppointmentEmailKind kind) => kind switch
        {
            AppointmentEmailKind.Confirmation => (
                "Your appointment is confirmed",
                "Confirmed",
                "#dcfce7",
                "#166534",
                "We have reserved your place. Here is everything you need to know before your visit.",
                "Please arrive 10 minutes before your appointment and bring a valid ID. If you need to change or cancel, contact us as soon as possible."),

            AppointmentEmailKind.Reschedule => (
                "Your appointment has been rescheduled",
                "Rescheduled",
                "#e0f2fe",
                "#075985",
                "Your appointment has a new date and time. Here are the updated details.",
                "Your previous booking has been replaced by the details above. Please arrive 10 minutes early and bring a valid ID."),

            AppointmentEmailKind.Cancellation => (
                "Your appointment has been cancelled",
                "Cancelled",
                "#ffe4e6",
                "#9f1239",
                "Your appointment has been cancelled. We are sorry to see you go.",
                "If you cancelled by mistake or would like to book a new appointment, our team will be happy to help."),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private string BuildHtml(
        MedicalAppointment appointment,
        Patient patient,
        Doctor doctor,
        AppointmentEmailKind kind,
        string dateText,
        string title,
        string badgeLabel,
        string badgeBackground,
        string badgeText,
        string intro,
        string note)
    {
        var hospitalName = Encode(_settings.HospitalName);
        var supportBlock = BuildSupportBlock();
        var statusLabel = GetStatusLabel(appointment.Status);
        var preheader = Encode(
            $"{title} with Dr. {doctor.Name} on {dateText}.");

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>{Encode(title)}</title>
            </head>
            <body style="margin: 0; padding: 0; background-color: #eef4fb; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #0f172a;">
              <div style="display: none; max-height: 0; overflow: hidden; opacity: 0;">
                {preheader}
              </div>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color: #eef4fb; padding: 32px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width: 100%; max-width: 600px; background-color: #ffffff; border-radius: 18px; overflow: hidden; box-shadow: 0 18px 45px rgba(15, 23, 42, 0.12);">
                      <tr>
                        <td style="background-color: #0b3b5c; background-image: linear-gradient(135deg, #0284c7 0%, #0b3b5c 100%); padding: 26px 32px;">
                          <table role="presentation" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="vertical-align: middle;">
                                <div style="display: inline-block; width: 44px; height: 44px; border-radius: 12px; background-color: rgba(255, 255, 255, 0.16); text-align: center; line-height: 44px; font-size: 26px; font-weight: 700; color: #ffffff;">+</div>
                              </td>
                              <td style="padding-left: 14px; vertical-align: middle;">
                                <div style="font-size: 20px; font-weight: 700; color: #ffffff; letter-spacing: 0.2px;">{hospitalName}</div>
                                <div style="font-size: 11px; color: #bae6fd; text-transform: uppercase; letter-spacing: 1.6px;">Clinical Appointments</div>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding: 28px 32px 0;">
                          <span style="display: inline-block; background-color: {badgeBackground}; color: {badgeText}; font-size: 11px; font-weight: 700; letter-spacing: 1.2px; text-transform: uppercase; padding: 7px 13px; border-radius: 999px;">{Encode(badgeLabel)}</span>
                          <h1 style="margin: 16px 0 8px; font-size: 26px; line-height: 1.25; color: #0f172a;">{Encode(title)}</h1>
                          <p style="margin: 0; font-size: 15px; line-height: 1.6; color: #475569;">{Encode(intro)}</p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding: 22px 32px 0;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 14px;">
                            <tr>
                              <td style="padding: 20px 22px;">
                                <div style="font-size: 11px; font-weight: 700; color: #0284c7; letter-spacing: 1.2px; text-transform: uppercase; margin-bottom: 10px;">Appointment details</div>
                                <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                                  {BuildDetailRow("Date and time", dateText)}
                                  {BuildDetailRow("Reference", $"#{appointment.Id}")}
                                  {BuildDetailRow("Specialty", doctor.Specialty)}
                                  {BuildDetailRow("Status", statusLabel)}
                                </table>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding: 16px 32px 0;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border: 1px solid #e2e8f0; border-radius: 14px;">
                            <tr>
                              <td style="padding: 16px 20px;">
                                <table role="presentation" cellpadding="0" cellspacing="0">
                                  <tr>
                                    <td style="width: 44px; vertical-align: middle;">
                                      <div style="width: 44px; height: 44px; border-radius: 50%; background-color: #e0f2fe; color: #0369a1; font-weight: 700; font-size: 16px; text-align: center; line-height: 44px;">{Encode(GetInitials(doctor.Name))}</div>
                                    </td>
                                    <td style="padding-left: 12px; vertical-align: middle;">
                                      <div style="font-weight: 700; color: #0f172a; font-size: 15px;">Dr. {Encode(doctor.Name)}</div>
                                      <div style="color: #64748b; font-size: 13px;">{Encode(doctor.Specialty)}</div>
                                    </td>
                                  </tr>
                                </table>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding: 22px 32px 0;">
                          <p style="margin: 0; font-size: 14px; line-height: 1.6; color: #475569;">{Encode(note)}</p>
                          {supportBlock}
                        </td>
                      </tr>

                      <tr>
                        <td style="padding: 26px 32px 30px;">
                          <div style="border-top: 1px solid #e2e8f0; padding-top: 18px; font-size: 12px; line-height: 1.6; color: #94a3b8;">
                            <strong style="color: #64748b;">{hospitalName}</strong><br>
                            This is an automated message, please do not reply.<br>
                            &copy; {DateTime.UtcNow.Year} {hospitalName}. All rights reserved.
                          </div>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string BuildDetailRow(string label, string value) => $"""
        <tr>
          <td style="padding: 7px 0; font-size: 13px; color: #64748b;">{Encode(label)}</td>
          <td style="padding: 7px 0; font-size: 14px; color: #0f172a; font-weight: 600; text-align: right;">{Encode(value)}</td>
        </tr>
        """;

    private string BuildSupportBlock()
    {
        var links = new List<string>();

        if (!string.IsNullOrWhiteSpace(_settings.SupportEmail))
        {
            links.Add(
                $"<a href=\"mailto:{Encode(_settings.SupportEmail)}\" style=\"color: #0284c7; text-decoration: none; font-weight: 600;\">{Encode(_settings.SupportEmail)}</a>");
        }

        if (!string.IsNullOrWhiteSpace(_settings.SupportPhone))
        {
            links.Add(
                $"<a href=\"tel:{Encode(_settings.SupportPhone)}\" style=\"color: #0284c7; text-decoration: none; font-weight: 600;\">{Encode(_settings.SupportPhone)}</a>");
        }

        return links.Count == 0
            ? string.Empty
            : $"<p style=\"margin: 14px 0 0; font-size: 13px; color: #64748b;\">Need help? Contact us at {string.Join(" or ", links)}.</p>";
    }

    private string BuildPlainText(
        MedicalAppointment appointment,
        Patient patient,
        Doctor doctor,
        string dateText,
        string title,
        string intro,
        string note)
    {
        var text = new StringBuilder();
        text.AppendLine(_settings.HospitalName.ToUpperInvariant());
        text.AppendLine(title);
        text.AppendLine();
        text.AppendLine($"Dear {patient.FullName},");
        text.AppendLine();
        text.AppendLine(intro);
        text.AppendLine();
        text.AppendLine($"Reference: #{appointment.Id}");
        text.AppendLine($"Date and time: {dateText}");
        text.AppendLine($"Doctor: Dr. {doctor.Name}");
        text.AppendLine($"Specialty: {doctor.Specialty}");
        text.AppendLine($"Status: {GetStatusLabel(appointment.Status)}");
        text.AppendLine();
        text.AppendLine(note);

        if (!string.IsNullOrWhiteSpace(_settings.SupportEmail) || !string.IsNullOrWhiteSpace(_settings.SupportPhone))
        {
            text.AppendLine();
            text.AppendLine(
                $"Need help? Contact us at {_settings.SupportEmail}{(string.IsNullOrWhiteSpace(_settings.SupportPhone) ? string.Empty : $" or {_settings.SupportPhone}")}.");
        }

        text.AppendLine();
        text.AppendLine("This is an automated message, please do not reply.");
        text.AppendLine($"(c) {DateTime.UtcNow.Year} {_settings.HospitalName}. All rights reserved.");
        return text.ToString();
    }

    private Attachment BuildCalendarAttachment(
        MedicalAppointment appointment,
        Doctor doctor,
        AppointmentEmailKind kind)
    {
        var startUtc = ToUtc(appointment.Date);
        var duration = _settings.AppointmentDurationMinutes > 0 ? _settings.AppointmentDurationMinutes : 30;
        var endUtc = startUtc.AddMinutes(duration);

        var description = kind == AppointmentEmailKind.Reschedule
            ? $"Rescheduled medical appointment. Specialty: {doctor.Specialty}. Reference #{appointment.Id}."
            : $"Medical appointment. Specialty: {doctor.Specialty}. Reference #{appointment.Id}.";

        var calendar = new StringBuilder();
        calendar.AppendLine("BEGIN:VCALENDAR");
        calendar.AppendLine("VERSION:2.0");
        calendar.AppendLine("PRODID:-//HospitalSV//Appointments//EN");
        calendar.AppendLine("CALSCALE:GREGORIAN");
        calendar.AppendLine($"METHOD:{(kind == AppointmentEmailKind.Cancellation ? "CANCEL" : "REQUEST")}");
        calendar.AppendLine("BEGIN:VEVENT");
        calendar.AppendLine($"UID:appointment-{appointment.Id}@hospitalsv.local");
        calendar.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
        calendar.AppendLine($"DTSTART:{startUtc:yyyyMMddTHHmmssZ}");
        calendar.AppendLine($"DTEND:{endUtc:yyyyMMddTHHmmssZ}");
        calendar.AppendLine($"SUMMARY:{EscapeIcs($"Medical appointment - Dr. {doctor.Name}")}");
        calendar.AppendLine($"DESCRIPTION:{EscapeIcs(description)}");
        calendar.AppendLine($"LOCATION:{EscapeIcs(_settings.HospitalName)}");
        calendar.AppendLine($"ORGANIZER;CN={EscapeIcs(_settings.HospitalName)}:mailto:{_settings.FromEmail}");
        if (!string.IsNullOrWhiteSpace(appointment.Patient?.Email))
        {
            calendar.AppendLine(
                $"ATTENDEE;CN={EscapeIcs(appointment.Patient.FullName)};RSVP=FALSE:mailto:{appointment.Patient.Email}");
        }
        calendar.AppendLine($"STATUS:{(kind == AppointmentEmailKind.Cancellation ? "CANCELLED" : "CONFIRMED")}");
        calendar.AppendLine($"SEQUENCE:{(kind == AppointmentEmailKind.Confirmation ? 0 : 1)}");
        calendar.AppendLine("END:VEVENT");
        calendar.AppendLine("END:VCALENDAR");

        var normalized = calendar
            .ToString()
            .Replace("\r\n", "\n")
            .Replace("\n", "\r\n");

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(normalized));
        var attachment = new Attachment(stream, "appointment.ics", "text/calendar")
        {
            TransferEncoding = TransferEncoding.Base64
        };

        return attachment;
    }

    private string GetEffectiveRecipient(string intendedRecipient)
        => string.IsNullOrWhiteSpace(_settings.RedirectAllTo)
            ? intendedRecipient
            : _settings.RedirectAllTo.Trim();

    private SmtpClient BuildClient()
    {
        var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout = 15_000
        };

        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
        }

        return client;
    }

    private static string GetStatusLabel(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Pending   => "Scheduled",
        AppointmentStatus.Attended  => "Attended",
        AppointmentStatus.Cancelled => "Cancelled",
        _                           => status.ToString()
    };

    private static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => string.Concat(parts[0][..1], parts[^1][..1]).ToUpperInvariant()
        };
    }

    private string FormatDisplayDate(DateTime utc)
    {
        var local = utc.AddMinutes(_settings.TimeZoneOffsetMinutes);
        var offset = TimeSpan.FromMinutes(_settings.TimeZoneOffsetMinutes);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        var label = $"GMT{sign}{absolute.Hours:00}:{absolute.Minutes:00}";

        return local.ToString(
            "dddd, dd MMMM yyyy 'at' HH:mm",
            CultureInfo.InvariantCulture) + $" ({label})";
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc   => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string EscapeIcs(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n")
        .Replace("\r", "\\n");

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
