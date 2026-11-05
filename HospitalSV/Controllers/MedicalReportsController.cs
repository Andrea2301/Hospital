using HospitalSV.Common;
using HospitalSV.Data;
using HospitalSV.DTOs;
using HospitalSV.Models;
using HospitalSV.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalSV.Controllers;

/// <summary>
/// Clinical Medical Reports and Encounters API.
/// Restricted strictly to Doctors and Administrators to protect patient medical privacy (HIPAA and Data Privacy).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Doctor")]
public class MedicalReportsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IPdfReportService _pdfReportService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<MedicalReportsController> _logger;

    public MedicalReportsController(
        AppDbContext context,
        IPdfReportService pdfReportService,
        ICurrentUserService currentUserService,
        ILogger<MedicalReportsController> logger)
    {
        _context = context;
        _pdfReportService = pdfReportService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    private static MedicalReportResponseDto ToDto(MedicalReport r)
    {
        decimal? bmi = null;
        if (r.WeightKg.HasValue && r.HeightCm.HasValue && r.HeightCm.Value > 0)
        {
            var heightM = r.HeightCm.Value / 100m;
            bmi = Math.Round(r.WeightKg.Value / (heightM * heightM), 1);
        }

        return new MedicalReportResponseDto(
            r.Id,
            r.AppointmentId,
            r.Appointment?.Date.ToString("yyyy-MM-ddTHH:mm:ss") ?? string.Empty,
            r.PatientId,
            r.Patient?.FullName ?? string.Empty,
            r.Patient?.Document ?? string.Empty,
            r.DoctorId,
            r.Doctor?.Name ?? string.Empty,
            r.Doctor?.Specialty ?? string.Empty,
            r.ReasonForVisit,
            r.PhysicalExamination,
            r.BloodPressure,
            r.HeartRate,
            r.Temperature,
            r.WeightKg,
            r.HeightCm,
            bmi,
            r.Diagnosis,
            r.Treatment,
            r.Notes,
            r.IsFinalized,
            r.CreatedAt,
            r.CreatedBy
        );
    }

    /// <summary>
    /// Retrieve medical reports with optional filtering (patientId, doctorId, search, pagination).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<MedicalReportResponseDto>>> GetAll(
        [FromQuery] int? patientId = null,
        [FromQuery] int? doctorId = null,
        [FromQuery] string? search = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = _context.MedicalReports
            .AsNoTracking()
            .Include(r => r.Appointment)
            .Include(r => r.Patient)
            .Include(r => r.Doctor)
            .AsQueryable();

        if (patientId.HasValue && patientId.Value > 0)
        {
            query = query.Where(r => r.PatientId == patientId.Value);
        }

        if (doctorId.HasValue && doctorId.Value > 0)
        {
            query = query.Where(r => r.DoctorId == doctorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var cleanSearch = SanitizationHelper.Clean(search).ToLower();
            query = query.Where(r =>
                r.Diagnosis.ToLower().Contains(cleanSearch) ||
                r.ReasonForVisit.ToLower().Contains(cleanSearch) ||
                (r.Patient != null && r.Patient.FullName.ToLower().Contains(cleanSearch)) ||
                (r.Doctor != null && r.Doctor.Name.ToLower().Contains(cleanSearch)));
        }

        var totalCount = await query.CountAsync();
        Response.Headers["X-Total-Count"] = totalCount.ToString();

        if (page.HasValue && pageSize.HasValue && page.Value > 0 && pageSize.Value > 0)
        {
            var take = Math.Min(pageSize.Value, 100);
            var skip = (page.Value - 1) * take;

            Response.Headers["X-Page"] = page.Value.ToString();
            Response.Headers["X-Page-Size"] = take.ToString();

            query = query.Skip(skip).Take(take);
        }

        var reports = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(reports.Select(ToDto));
    }

    /// <summary>Get a single medical report by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<MedicalReportResponseDto>> GetById(int id)
    {
        var report = await _context.MedicalReports
            .AsNoTracking()
            .Include(r => r.Appointment)
            .Include(r => r.Patient)
            .Include(r => r.Doctor)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (report is null)
            return NotFound(new { message = $"Medical report with ID {id} was not found." });

        return Ok(ToDto(report));
    }

    /// <summary>Get clinical report for a specific appointment ID.</summary>
    [HttpGet("appointment/{appointmentId:int}")]
    public async Task<ActionResult<MedicalReportResponseDto>> GetByAppointmentId(int appointmentId)
    {
        var report = await _context.MedicalReports
            .AsNoTracking()
            .Include(r => r.Appointment)
            .Include(r => r.Patient)
            .Include(r => r.Doctor)
            .FirstOrDefaultAsync(r => r.AppointmentId == appointmentId);

        if (report is null)
            return NotFound(new { message = $"No medical report has been generated for appointment #{appointmentId} yet." });

        return Ok(ToDto(report));
    }

    /// <summary>Get the full medical history (reports) for a patient.</summary>
    [HttpGet("patient/{patientId:int}")]
    public async Task<ActionResult<IEnumerable<MedicalReportResponseDto>>> GetByPatientId(int patientId)
    {
        var patientExists = await _context.Patients.AnyAsync(p => p.Id == patientId);
        if (!patientExists)
            return NotFound(new { message = $"Patient with ID {patientId} was not found." });

        var reports = await _context.MedicalReports
            .AsNoTracking()
            .Include(r => r.Appointment)
            .Include(r => r.Patient)
            .Include(r => r.Doctor)
            .Where(r => r.PatientId == patientId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(reports.Select(ToDto));
    }

    /// <summary>
    /// Create a new medical report for an appointment.
    /// Automatically marks the appointment status as 'Attended'.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MedicalReportResponseDto>> Create([FromBody] MedicalReportCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var appointment = await _context.MedicalAppointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.Id == dto.AppointmentId);

        if (appointment is null)
            return NotFound(new { message = $"Appointment with ID {dto.AppointmentId} was not found." });

        if (appointment.Status == AppointmentStatus.Cancelled)
            return BadRequest(new { message = "Cannot create a medical report for a cancelled appointment." });

        var existingReport = await _context.MedicalReports.AnyAsync(r => r.AppointmentId == dto.AppointmentId);
        if (existingReport)
            return Conflict(new { message = "A medical report already exists for this appointment." });

        var report = new MedicalReport
        {
            AppointmentId       = appointment.Id,
            PatientId           = appointment.PatientId,
            DoctorId            = appointment.DoctorId,
            ReasonForVisit      = SanitizationHelper.Clean(dto.ReasonForVisit),
            PhysicalExamination = SanitizationHelper.Clean(dto.PhysicalExamination),
            BloodPressure       = SanitizationHelper.Clean(dto.BloodPressure),
            HeartRate           = dto.HeartRate,
            Temperature         = dto.Temperature,
            WeightKg            = dto.WeightKg,
            HeightCm            = dto.HeightCm,
            Diagnosis           = SanitizationHelper.Clean(dto.Diagnosis),
            Treatment           = SanitizationHelper.Clean(dto.Treatment),
            Notes               = SanitizationHelper.Clean(dto.Notes),
            IsFinalized         = dto.IsFinalized
        };

        // Automatic business state transition: Mark appointment as Attended
        appointment.Status = AppointmentStatus.Attended;

        _context.MedicalReports.Add(report);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Medical report created: ID {ReportId} for Appointment #{AppointmentId} by user '{User}'.",
            report.Id, report.AppointmentId, _currentUserService.UserEmail);

        // Load navigations for response DTO
        report.Appointment = appointment;
        report.Patient = appointment.Patient;
        report.Doctor = appointment.Doctor;

        var response = ToDto(report);
        return CreatedAtAction(nameof(GetById), new { id = report.Id }, response);
    }

    /// <summary>Update an existing medical report.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] MedicalReportUpdateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var report = await _context.MedicalReports.FindAsync(id);
        if (report is null)
            return NotFound(new { message = $"Medical report with ID {id} was not found." });

        report.ReasonForVisit      = SanitizationHelper.Clean(dto.ReasonForVisit);
        report.PhysicalExamination = SanitizationHelper.Clean(dto.PhysicalExamination);
        report.BloodPressure       = SanitizationHelper.Clean(dto.BloodPressure);
        report.HeartRate           = dto.HeartRate;
        report.Temperature         = dto.Temperature;
        report.WeightKg            = dto.WeightKg;
        report.HeightCm            = dto.HeightCm;
        report.Diagnosis           = SanitizationHelper.Clean(dto.Diagnosis);
        report.Treatment           = SanitizationHelper.Clean(dto.Treatment);
        report.Notes               = SanitizationHelper.Clean(dto.Notes);
        report.IsFinalized         = dto.IsFinalized;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Medical report ID {ReportId} updated by user '{User}'.", report.Id, _currentUserService.UserEmail);

        return NoContent();
    }

    /// <summary>
    /// Generate and download the official medical report as a high-fidelity PDF.
    /// </summary>
    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> DownloadPdf(int id)
    {
        var report = await _context.MedicalReports
            .AsNoTracking()
            .Include(r => r.Appointment)
            .Include(r => r.Patient)
            .Include(r => r.Doctor)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (report is null)
            return NotFound(new { message = $"Medical report with ID {id} was not found." });

        var dto = ToDto(report);
        var pdfBytes = _pdfReportService.GenerateMedicalReportPdf(dto);

        var fileName = $"Informe_Medico_{report.Id}_{report.Patient?.Document ?? "PTE"}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    /// <summary>Soft delete a medical report (Admin only).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var report = await _context.MedicalReports.FindAsync(id);
        if (report is null)
            return NotFound(new { message = $"Medical report with ID {id} was not found." });

        _context.MedicalReports.Remove(report);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Medical report ID {ReportId} soft-deleted by Admin '{User}'.", report.Id, _currentUserService.UserEmail);
        return NoContent();
    }
}
