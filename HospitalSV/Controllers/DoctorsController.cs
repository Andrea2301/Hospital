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
public class DoctorsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<DoctorsController> _logger;

    public DoctorsController(AppDbContext context, ILogger<DoctorsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all active doctors with optional search, specialty filtering, and pagination.
    /// Returns IEnumerable of DoctorResponseDto for seamless frontend compatibility.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DoctorResponseDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] string? specialty = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = _context.Doctors.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var cleanSearch = SanitizationHelper.Clean(search).ToLower();
            query = query.Where(d =>
                d.Name.ToLower().Contains(cleanSearch) ||
                d.Document.ToLower().Contains(cleanSearch) ||
                d.Email.ToLower().Contains(cleanSearch) ||
                d.Specialty.ToLower().Contains(cleanSearch));
        }

        if (!string.IsNullOrWhiteSpace(specialty))
        {
            var cleanSpecialty = SanitizationHelper.Clean(specialty).ToLower();
            query = query.Where(d => d.Specialty.ToLower().Contains(cleanSpecialty));
        }

        var totalCount = await query.CountAsync();
        Response.Headers["X-Total-Count"] = totalCount.ToString();

        if (page.HasValue && pageSize.HasValue && page.Value > 0 && pageSize.Value > 0)
        {
            var take = Math.Min(pageSize.Value, 200); // Cap to prevent memory exhaustion DoS
            var skip = (page.Value - 1) * take;

            Response.Headers["X-Page"] = page.Value.ToString();
            Response.Headers["X-Page-Size"] = take.ToString();

            query = query.Skip(skip).Take(take);
        }

        var doctors = await query
            .OrderBy(d => d.Name)
            .Select(d => new DoctorResponseDto(d.Id, d.Name, d.Document, d.Specialty, d.Phone, d.Email))
            .ToListAsync();

        return Ok(doctors);
    }

    /// <summary>Get a single doctor by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<DoctorResponseDto>> GetById(int id)
    {
        var doctor = await _context.Doctors
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id);

        if (doctor is null)
            return NotFound(new { message = $"Doctor with ID {id} was not found." });

        return Ok(new DoctorResponseDto(doctor.Id, doctor.Name, doctor.Document, doctor.Specialty, doctor.Phone, doctor.Email));
    }

    /// <summary>Create a new doctor (Admin only).</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<DoctorResponseDto>> Create([FromBody] DoctorCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var cleanDocument = SanitizationHelper.Clean(dto.Document);
        var cleanName     = SanitizationHelper.Clean(dto.Name);
        var cleanSpecialty = SanitizationHelper.Clean(dto.Specialty);
        var cleanPhone    = SanitizationHelper.Clean(dto.Phone);
        var cleanEmail    = SanitizationHelper.NormalizeEmail(dto.Email);

        var documentExists = await _context.Doctors.AnyAsync(d => d.Document == cleanDocument);
        if (documentExists)
            return Conflict(new { message = "A doctor with this document number already exists." });

        var doctor = new Doctor
        {
            Name      = cleanName,
            Document  = cleanDocument,
            Specialty = cleanSpecialty,
            Phone     = cleanPhone,
            Email     = cleanEmail
        };

        _context.Doctors.Add(doctor);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Doctor created: ID {DoctorId}, Document '{Document}'.", doctor.Id, doctor.Document);

        var response = new DoctorResponseDto(doctor.Id, doctor.Name, doctor.Document, doctor.Specialty, doctor.Phone, doctor.Email);
        return CreatedAtAction(nameof(GetById), new { id = doctor.Id }, response);
    }

    /// <summary>Update an existing doctor (Admin only).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] DoctorUpdateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var doctor = await _context.Doctors.FindAsync(id);
        if (doctor is null)
            return NotFound(new { message = $"Doctor with ID {id} was not found." });

        doctor.Name      = SanitizationHelper.Clean(dto.Name);
        doctor.Specialty = SanitizationHelper.Clean(dto.Specialty);
        doctor.Phone     = SanitizationHelper.Clean(dto.Phone);
        doctor.Email     = SanitizationHelper.NormalizeEmail(dto.Email);

        await _context.SaveChangesAsync();
        _logger.LogInformation("Doctor updated: ID {DoctorId}.", doctor.Id);

        return NoContent();
    }

    /// <summary>Soft delete a doctor record (Admin only).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var doctor = await _context.Doctors.FindAsync(id);
        if (doctor is null)
            return NotFound(new { message = $"Doctor with ID {id} was not found." });

        // Cancel pending appointments for this doctor
        var pendingAppointments = await _context.MedicalAppointments
            .Where(a => a.DoctorId == id && a.Status == AppointmentStatus.Pending)
            .ToListAsync();

        foreach (var apt in pendingAppointments)
        {
            apt.Status = AppointmentStatus.Cancelled;
            _context.MedicalAppointments.Remove(apt);
        }

        _context.Doctors.Remove(doctor);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Doctor deleted (soft): ID {DoctorId}.", doctor.Id);
        return NoContent();
    }
}
