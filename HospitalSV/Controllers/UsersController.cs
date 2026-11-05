using HospitalSV.Common;
using HospitalSV.Data;
using HospitalSV.DTOs;
using HospitalSV.Models;
using HospitalSV.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalSV.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UsersController> _logger;

    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin", "Doctor", "Receptionist"
    };

    public UsersController(
        AppDbContext context,
        ICurrentUserService currentUserService,
        ILogger<UsersController> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    /// <summary>Get all staff users with optional search filtering (Admin only).</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponseDto>>> GetAll([FromQuery] string? search = null)
    {
        var query = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var cleanSearch = SanitizationHelper.Clean(search).ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(cleanSearch) ||
                u.Email.ToLower().Contains(cleanSearch) ||
                u.Role.ToLower().Contains(cleanSearch));
        }

        var users = await query
            .OrderBy(u => u.Id)
            .Select(u => new UserResponseDto(
                u.Id,
                u.FullName,
                u.Email,
                u.Role,
                u.IsActive,
                u.CreatedAt,
                u.CreatedBy
            ))
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>Get user details by ID (Admin only).</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserResponseDto>> GetById(int id)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null)
            return NotFound(new { message = $"User with ID {id} was not found." });

        return Ok(new UserResponseDto(
            user.Id,
            user.FullName,
            user.Email,
            user.Role,
            user.IsActive,
            user.CreatedAt,
            user.CreatedBy
        ));
    }

    /// <summary>Create a new staff user with an assigned role (Admin only).</summary>
    [HttpPost]
    public async Task<ActionResult<UserResponseDto>> Create([FromBody] UserCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var cleanRole = SanitizationHelper.Clean(dto.Role);
        if (!ValidRoles.Contains(cleanRole))
            return BadRequest(new { message = $"Invalid role '{dto.Role}'. Allowed roles: Admin, Doctor, Receptionist." });

        var cleanEmail = SanitizationHelper.NormalizeEmail(dto.Email);
        var cleanName = SanitizationHelper.Clean(dto.FullName);

        var emailExists = await _context.Users.AnyAsync(u => u.Email == cleanEmail);
        if (emailExists)
            return Conflict(new { message = "A user with this email already exists." });

        var user = new AppUser
        {
            FullName     = cleanName,
            Email        = cleanEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role         = cleanRole,
            IsActive     = true
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin created user '{Email}' with role '{Role}'.", user.Email, user.Role);

        var response = new UserResponseDto(
            user.Id,
            user.FullName,
            user.Email,
            user.Role,
            user.IsActive,
            user.CreatedAt,
            user.CreatedBy
        );

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, response);
    }

    /// <summary>Update the role of an existing user (Admin only).</summary>
    [HttpPut("{id:int}/role")]
    public async Task<IActionResult> UpdateRole(int id, [FromBody] UserUpdateRoleDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var cleanRole = SanitizationHelper.Clean(dto.Role);
        if (!ValidRoles.Contains(cleanRole))
            return BadRequest(new { message = $"Invalid role '{dto.Role}'. Allowed roles: Admin, Doctor, Receptionist." });

        var user = await _context.Users.FindAsync(id);
        if (user is null)
            return NotFound(new { message = $"User with ID {id} was not found." });

        // Prevent self-demotion from Admin
        var currentEmail = _currentUserService.UserEmail;
        if (currentEmail?.Equals(user.Email, StringComparison.OrdinalIgnoreCase) == true &&
            !cleanRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "You cannot remove your own Admin role." });
        }

        // Prevent demoting the last active Admin
        if (user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
            !cleanRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            var activeAdminCount = await _context.Users.CountAsync(u => u.Role == "Admin" && u.IsActive);
            if (activeAdminCount <= 1)
            {
                return BadRequest(new { message = "Cannot demote the last remaining active Administrator in the system." });
            }
        }

        user.Role = cleanRole;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Role updated for user '{Email}' to '{Role}' by Admin.", user.Email, user.Role);
        return NoContent();
    }

    /// <summary>Toggle active status of a user (Admin only).</summary>
    [HttpPatch("{id:int}/toggle-status")]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user is null)
            return NotFound(new { message = $"User with ID {id} was not found." });

        var currentEmail = _currentUserService.UserEmail;
        if (currentEmail?.Equals(user.Email, StringComparison.OrdinalIgnoreCase) == true)
            return BadRequest(new { message = "You cannot deactivate your own account." });

        // Prevent deactivating the last active Admin
        if (user.IsActive && user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            var activeAdminCount = await _context.Users.CountAsync(u => u.Role == "Admin" && u.IsActive);
            if (activeAdminCount <= 1)
            {
                return BadRequest(new { message = "Cannot deactivate the last remaining active Administrator in the system." });
            }
        }

        user.IsActive = !user.IsActive;
        await _context.SaveChangesAsync();

        _logger.LogInformation("User '{Email}' status toggled to Active={IsActive}.", user.Email, user.IsActive);
        return Ok(new { message = $"User {(user.IsActive ? "activated" : "deactivated")} successfully.", isActive = user.IsActive });
    }

    /// <summary>Delete a user account (Admin only).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user is null)
            return NotFound(new { message = $"User with ID {id} was not found." });

        var currentEmail = _currentUserService.UserEmail;
        if (currentEmail?.Equals(user.Email, StringComparison.OrdinalIgnoreCase) == true)
            return BadRequest(new { message = "You cannot delete your own account." });

        // Prevent deleting the last active Admin
        if (user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            var activeAdminCount = await _context.Users.CountAsync(u => u.Role == "Admin" && u.IsActive);
            if (activeAdminCount <= 1)
            {
                return BadRequest(new { message = "Cannot delete the last remaining active Administrator in the system." });
            }
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User '{Email}' was deleted by Admin.", user.Email);
        return NoContent();
    }
}
