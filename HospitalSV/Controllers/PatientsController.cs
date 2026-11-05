using HospitalSV.Common;
using HospitalSV.Data;
using HospitalSV.DTOs;
using HospitalSV.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalSV.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PatientsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<PatientsController> _logger;

    public PatientsController(AppDbContext context, ILogger<PatientsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all active patients with optional search and pagination.
    /// Returns IEnumerable of PatientResponseDto for seamless frontend compatibility.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PatientResponseDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = _context.Patients.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var cleanSearch = SanitizationHelper.Clean(search).ToLower();
            query = query.Where(p =>
                p.FullName.ToLower().Contains(cleanSearch) ||
                p.Document.ToLower().Contains(cleanSearch) ||
                p.Email.ToLower().Contains(cleanSearch) ||
                p.Phone.ToLower().Contains(cleanSearch));
        }

        var totalCount = await query.CountAsync();
        Response.Headers["X-Total-Count"] = totalCount.ToString();

        if (page.HasValue && pageSize.HasValue && page.Value > 0 && pageSize.Value > 0)
        {
            var take = Math.Min(pageSize.Value, 200); // Cap pageSize to prevent DoS
            var skip = (page.Value - 1) * take;

            Response.Headers["X-Page"] = page.Value.ToString();
            Response.Headers["X-Page-Size"] = take.ToString();

            query = query.Skip(skip).Take(take);
        }

        var patients = await query
            .OrderBy(p => p.FullName)
            .Select(p => new PatientResponseDto(p.Id, p.FullName, p.Document, p.Phone, p.Email))
            .ToListAsync();

        return Ok(patients);
    }

    /// <summary>Get a single patient by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PatientResponseDto>> GetById(int id)
    {
        var patient = await _context.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (patient is null)
            return NotFound(new { message = $"Patient with ID {id} was not found." });

        return Ok(new PatientResponseDto(patient.Id, patient.FullName, patient.Document, patient.Phone, patient.Email));
    }

    /// <summary>Register a new patient admission (Admin or Receptionist).</summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Receptionist")]
    public async Task<ActionResult<PatientResponseDto>> Create([FromBody] PatientCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var cleanDocument = SanitizationHelper.Clean(dto.Document);
        var cleanFullName = SanitizationHelper.Clean(dto.FullName);
        var cleanPhone    = SanitizationHelper.Clean(dto.Phone);
        var cleanEmail    = SanitizationHelper.NormalizeEmail(dto.Email);

        var documentExists = await _context.Patients.AnyAsync(p => p.Document == cleanDocument);
        if (documentExists)
            return Conflict(new { message = "A patient with this document number already exists." });

        var patient = new Patient
        {
            FullName = cleanFullName,
            Document = cleanDocument,
            Phone    = cleanPhone,
            Email    = cleanEmail
        };

        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Patient created: ID {PatientId}, Document '{Document}'.", patient.Id, patient.Document);

        var response = new PatientResponseDto(patient.Id, patient.FullName, patient.Document, patient.Phone, patient.Email);
        return CreatedAtAction(nameof(GetById), new { id = patient.Id }, response);
    }

    /// <summary>Update an existing patient record (Admin or Receptionist).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Receptionist")]
    public async Task<IActionResult> Update(int id, [FromBody] PatientUpdateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var patient = await _context.Patients.FindAsync(id);
        if (patient is null)
            return NotFound(new { message = $"Patient with ID {id} was not found." });

        patient.FullName = SanitizationHelper.Clean(dto.FullName);
        patient.Phone    = SanitizationHelper.Clean(dto.Phone);
        patient.Email    = SanitizationHelper.NormalizeEmail(dto.Email);

        await _context.SaveChangesAsync();
        _logger.LogInformation("Patient updated: ID {PatientId}.", patient.Id);

        return NoContent();
    }

    /// <summary>Soft delete an archived patient record (Admin only).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var patient = await _context.Patients.FindAsync(id);
        if (patient is null)
            return NotFound(new { message = $"Patient with ID {id} was not found." });

        // Cancel all pending appointments for this patient
        var pendingAppointments = await _context.MedicalAppointments
            .Where(a => a.PatientId == id && a.Status == AppointmentStatus.Pending)
            .ToListAsync();

        foreach (var apt in pendingAppointments)
        {
            apt.Status = AppointmentStatus.Cancelled;
            _context.MedicalAppointments.Remove(apt);
        }

        _context.Patients.Remove(patient);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Patient deleted (soft): ID {PatientId}.", patient.Id);
        return NoContent();
    }
}
