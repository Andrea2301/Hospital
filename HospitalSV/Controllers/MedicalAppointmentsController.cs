using System.Globalization;
using HospitalSV.Common;
using HospitalSV.Data;
using HospitalSV.DTOs;
using HospitalSV.Models;
using HospitalSV.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HospitalSV.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MedicalAppointmentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IEmailService _emailService;
    private readonly DemoOptions _demo;
    private readonly ILogger<MedicalAppointmentsController> _logger;

    public MedicalAppointmentsController(
        AppDbContext context,
        ICurrentUserService currentUserService,
        IEmailService emailService,
        IOptions<DemoOptions> demo,
        ILogger<MedicalAppointmentsController> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _emailService = emailService;
        _demo = demo.Value;
        _logger = logger;
    }

    /// <summary>
    /// Normalizes any incoming DateTime to UTC. Values without an explicit kind
    /// (e.g. "2026-11-04T10:00:00") are treated as UTC, which is the API contract
    /// and avoids Npgsql rejecting them for a 'timestamp with time zone' column.
    /// </summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc   => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string ToUtcIsoString(DateTime value) =>
        ToUtc(value).ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static AppointmentResponseDto ToDto(MedicalAppointment a) => new(
        a.Id,
        ToUtcIsoString(a.Date),
        a.Status.ToString(),
        a.PatientId,
        a.Patient?.FullName ?? string.Empty,
        a.DoctorId,
        a.Doctor?.Name      ?? string.Empty,
        a.Doctor?.Specialty ?? string.Empty
    );

    /// <summary>
    /// Admins and receptionists can manage any appointment. A doctor can only
    /// manage the appointments assigned to the Doctor record matching their email.
    /// </summary>
    private async Task<bool> CanManageAppointmentAsync(MedicalAppointment appointment)
    {
        if (User.IsInRole("Admin") || User.IsInRole("Receptionist"))
            return true;

        var email = _currentUserService.UserEmail;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var normalizedEmail = email.Trim().ToLowerInvariant();

        return await _context.Doctors
            .AsNoTracking()
            .AnyAsync(d => d.Id == appointment.DoctorId && d.Email.ToLower() == normalizedEmail);
    }

    /// <summary>
    /// Sends the confirmation email as a best-effort side effect. Email problems
    /// must never fail or roll back the appointment operation.
    /// </summary>
    private async Task<bool> SendConfirmationEmailAsync(MedicalAppointment appointment, bool isReschedule)
    {
        if (_demo.Enabled)
            return false;

        try
        {
            return await _emailService.SendAppointmentConfirmationAsync(appointment, isReschedule);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while sending appointment #{AppointmentId} confirmation email.",
                appointment.Id);

            return false;
        }
    }

    private async Task<bool> SendCancellationEmailAsync(MedicalAppointment appointment)
    {
        if (_demo.Enabled)
            return false;

        try
        {
            return await _emailService.SendAppointmentCancellationAsync(appointment);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while sending appointment #{AppointmentId} cancellation email.",
                appointment.Id);

            return false;
        }
    }

    /// <summary>
    /// Get medical appointments with optional filters (status, doctor, patient, date range) and pagination.
    /// Returns IEnumerable of AppointmentResponseDto for frontend compatibility.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AppointmentResponseDto>>> GetAll(
        [FromQuery] string? status = null,
        [FromQuery] int? doctorId = null,
        [FromQuery] int? patientId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        IQueryable<MedicalAppointment> query = _context.MedicalAppointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AppointmentStatus>(status, true, out var parsedStatus) ||
                !Enum.IsDefined(parsedStatus))
            {
                return BadRequest(new
                {
                    message = $"Invalid status '{status}'. Valid values are Pending, Attended and Cancelled."
                });
            }

            query = query.Where(a => a.Status == parsedStatus);
        }

        if (doctorId.HasValue && doctorId.Value > 0)
        {
            query = query.Where(a => a.DoctorId == doctorId.Value);
        }

        if (patientId.HasValue && patientId.Value > 0)
        {
            query = query.Where(a => a.PatientId == patientId.Value);
        }

        if (fromDate.HasValue)
        {
            var fromUtc = ToUtc(fromDate.Value);
            query = query.Where(a => a.Date >= fromUtc);
        }

        if (toDate.HasValue)
        {
            var toUtc = ToUtc(toDate.Value);
            query = query.Where(a => a.Date <= toUtc);
        }

        var totalCount = await query.CountAsync();
        Response.Headers["X-Total-Count"] = totalCount.ToString();

        // Ordering must be applied before Skip/Take so the paginated window is stable.
        query = query.OrderByDescending(a => a.Date);

        if (page.HasValue && pageSize.HasValue && page.Value > 0 && pageSize.Value > 0)
        {
            var take = Math.Min(pageSize.Value, 200);
            var skip = (int)Math.Min((long)(page.Value - 1) * take, int.MaxValue);

            Response.Headers["X-Page"] = page.Value.ToString();
            Response.Headers["X-Page-Size"] = take.ToString();

            query = query.Skip(skip).Take(take);
        }

        var appointments = await query.ToListAsync();

        return Ok(appointments.Select(ToDto));
    }

    /// <summary>Get a single appointment by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AppointmentResponseDto>> GetById(int id)
    {
        var appointment = await _context.MedicalAppointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (appointment is null)
            return NotFound(new { message = $"Appointment with ID {id} was not found." });

        return Ok(ToDto(appointment));
    }

    /// <summary>Schedule a new medical appointment (Admin or Receptionist).</summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Receptionist")]
    public async Task<ActionResult<AppointmentResponseDto>> Create([FromBody] AppointmentCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var scheduledAt = ToUtc(dto.Date);

        // Security / Business rule: Cannot schedule appointments in the past
        if (scheduledAt < DateTime.UtcNow.AddMinutes(-10))
        {
            return BadRequest(new { message = "Cannot schedule an appointment in the past." });
        }

        // Business rule: Appointments cannot be booked more than 1 year into the future
        if (scheduledAt > DateTime.UtcNow.AddYears(1))
        {
            return BadRequest(new { message = "Appointments cannot be scheduled more than 1 year in advance." });
        }

        var patient = await _context.Patients.FindAsync(dto.PatientId);
        if (patient is null)
            return NotFound(new { message = "Selected patient was not found." });

        var doctor = await _context.Doctors.FindAsync(dto.DoctorId);
        if (doctor is null)
            return NotFound(new { message = "Selected doctor was not found." });

        // Conflict check: doctor or patient already has an active appointment at that time
        var conflict = await _context.MedicalAppointments.AnyAsync(a =>
            a.Date == scheduledAt &&
            (a.DoctorId == dto.DoctorId || a.PatientId == dto.PatientId) &&
            a.Status != AppointmentStatus.Cancelled
        );

        if (conflict)
            return Conflict(new { message = "The doctor or patient already has an active appointment at the selected date and time." });

        var appointment = new MedicalAppointment
        {
            Date      = scheduledAt,
            PatientId = dto.PatientId,
            DoctorId  = dto.DoctorId,
            Status    = AppointmentStatus.Pending
        };

        _context.MedicalAppointments.Add(appointment);
        await _context.SaveChangesAsync();

        await _context.Entry(appointment).Reference(a => a.Patient).LoadAsync();
        await _context.Entry(appointment).Reference(a => a.Doctor).LoadAsync();

        _logger.LogInformation("Appointment scheduled: ID {AppointmentId} for Patient {PatientId} with Doctor {DoctorId}.", appointment.Id, dto.PatientId, dto.DoctorId);

        await SendConfirmationEmailAsync(appointment, isReschedule: false);

        return CreatedAtAction(nameof(GetById), new { id = appointment.Id }, ToDto(appointment));
    }

    /// <summary>Reschedule an existing appointment (Admin or Receptionist).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Receptionist")]
    public async Task<IActionResult> Update(int id, [FromBody] AppointmentUpdateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var appointment = await _context.MedicalAppointments.FindAsync(id);
        if (appointment is null)
            return NotFound(new { message = $"Appointment with ID {id} was not found." });

        if (appointment.Status == AppointmentStatus.Cancelled)
            return BadRequest(new { message = "A cancelled appointment cannot be modified." });

        if (appointment.Status == AppointmentStatus.Attended)
            return BadRequest(new { message = "An attended appointment cannot be modified." });

        var scheduledAt = ToUtc(dto.Date);

        if (scheduledAt < DateTime.UtcNow.AddMinutes(-10))
            return BadRequest(new { message = "Cannot reschedule an appointment to a past date." });

        // Keep the same booking horizon used when creating an appointment.
        if (scheduledAt > DateTime.UtcNow.AddYears(1))
            return BadRequest(new { message = "Appointments cannot be scheduled more than 1 year in advance." });

        var patientExists = await _context.Patients.AnyAsync(p => p.Id == dto.PatientId);
        if (!patientExists)
            return NotFound(new { message = "Selected patient was not found." });

        var doctorExists = await _context.Doctors.AnyAsync(d => d.Id == dto.DoctorId);
        if (!doctorExists)
            return NotFound(new { message = "Selected doctor was not found." });

        var conflict = await _context.MedicalAppointments.AnyAsync(a =>
            a.Id != id &&
            a.Date == scheduledAt &&
            (a.DoctorId == dto.DoctorId || a.PatientId == dto.PatientId) &&
            a.Status != AppointmentStatus.Cancelled
        );

        if (conflict)
            return Conflict(new { message = "The doctor or patient already has an active appointment at the selected date and time." });

        appointment.Date      = scheduledAt;
        appointment.PatientId = dto.PatientId;
        appointment.DoctorId  = dto.DoctorId;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Appointment updated: ID {AppointmentId}.", appointment.Id);

        // Load contact details required by the reschedule confirmation email.
        await _context.Entry(appointment).Reference(a => a.Patient).LoadAsync();
        await _context.Entry(appointment).Reference(a => a.Doctor).LoadAsync();

        await SendConfirmationEmailAsync(appointment, isReschedule: true);

        return NoContent();
    }

    /// <summary>Resend the confirmation email for an existing appointment.</summary>
    [HttpPost("{id:int}/send-confirmation")]
    [Authorize(Roles = "Admin,Doctor,Receptionist")]
    public async Task<IActionResult> SendConfirmation(int id)
    {
        var appointment = await _context.MedicalAppointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (appointment is null)
            return NotFound(new { message = $"Appointment with ID {id} was not found." });

        if (!await CanManageAppointmentAsync(appointment))
            return Forbid();

        if (appointment.Status == AppointmentStatus.Cancelled)
            return BadRequest(new { message = "A confirmation cannot be sent for a cancelled appointment." });

        if (_demo.Enabled)
            return Ok(new { message = "Email delivery is disabled in the demo sandbox." });

        var sent = await SendConfirmationEmailAsync(appointment, isReschedule: false);

        if (!sent)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "The confirmation email could not be sent. Check the email configuration and logs." });
        }

        return Ok(new { message = $"Confirmation email sent to {appointment.Patient?.Email}." });
    }

    /// <summary>Cancel an appointment (Admin, Doctor, or Receptionist).</summary>
    [HttpPatch("{id:int}/cancel")]
    [Authorize(Roles = "Admin,Doctor,Receptionist")]
    public async Task<IActionResult> Cancel(int id)
    {
        var appointment = await _context.MedicalAppointments.FindAsync(id);
        if (appointment is null)
            return NotFound(new { message = $"Appointment with ID {id} was not found." });

        if (!await CanManageAppointmentAsync(appointment))
            return Forbid();

        if (appointment.Status == AppointmentStatus.Cancelled)
            return BadRequest(new { message = "Appointment is already cancelled." });

        if (appointment.Status == AppointmentStatus.Attended)
            return BadRequest(new { message = "Cannot cancel an appointment that has already been attended." });

        appointment.Status = AppointmentStatus.Cancelled;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Appointment cancelled: ID {AppointmentId}.", appointment.Id);

        await _context.Entry(appointment).Reference(a => a.Patient).LoadAsync();
        await _context.Entry(appointment).Reference(a => a.Doctor).LoadAsync();

        await SendCancellationEmailAsync(appointment);

        return NoContent();
    }

    /// <summary>Mark an appointment as attended (Admin or Doctor only).</summary>
    [HttpPatch("{id:int}/attend")]
    [Authorize(Roles = "Admin,Doctor")]
    public async Task<IActionResult> Attend(int id)
    {
        var appointment = await _context.MedicalAppointments.FindAsync(id);
        if (appointment is null)
            return NotFound(new { message = $"Appointment with ID {id} was not found." });

        if (!await CanManageAppointmentAsync(appointment))
            return Forbid();

        if (appointment.Status == AppointmentStatus.Attended)
            return BadRequest(new { message = "Appointment is already marked as attended." });

        if (appointment.Status == AppointmentStatus.Cancelled)
            return BadRequest(new { message = "A cancelled appointment cannot be marked as attended." });

        appointment.Status = AppointmentStatus.Attended;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Appointment marked as attended: ID {AppointmentId}.", appointment.Id);
        return NoContent();
    }

    /// <summary>Soft delete an appointment record (Admin or Receptionist).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Receptionist")]
    public async Task<IActionResult> Delete(int id)
    {
        var appointment = await _context.MedicalAppointments.FindAsync(id);
        if (appointment is null)
            return NotFound(new { message = $"Appointment with ID {id} was not found." });

        _context.MedicalAppointments.Remove(appointment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Appointment deleted (soft): ID {AppointmentId}.", appointment.Id);
        return NoContent();
    }
}
