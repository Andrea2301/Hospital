using HospitalSV.Common;
using HospitalSV.Data;
using HospitalSV.DTOs;
using HospitalSV.Models;
using HospitalSV.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HospitalSV.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IJwtService  _jwtService;
    private readonly DemoOptions  _demo;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext context,
        IJwtService jwtService,
        IOptions<DemoOptions> demo,
        ILogger<AuthController> logger)
    {
        _context    = context;
        _jwtService = jwtService;
        _demo       = demo.Value;
        _logger     = logger;
    }

    /// <summary>
    /// Register a new user account.
    /// Default role is strictly 'Receptionist' to eliminate role privilege escalation attacks.
    /// Only an Admin can promote accounts via /api/users/{id}/role.
    /// Registration is disabled while the public sandbox is active.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (_demo.Enabled)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Account registration is disabled in the demo sandbox." });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var cleanName  = SanitizationHelper.Clean(dto.FullName);
        var cleanEmail = SanitizationHelper.NormalizeEmail(dto.Email);

        var emailTaken = await _context.Users.AnyAsync(u => u.Email == cleanEmail);
        if (emailTaken)
        {
            _logger.LogWarning("Registration failed: Email '{Email}' is already registered.", cleanEmail);
            return Conflict(new { message = "Email is already registered." });
        }

        // Automatic fallback: First user ever gets Admin to bootstrap the system, all subsequent public registrations are Receptionist.
        // Role cannot be injected or modified via JSON payload.
        var isFirstUser = !await _context.Users.AnyAsync();
        var assignedRole = isFirstUser ? "Admin" : "Receptionist";

        var user = new AppUser
        {
            FullName     = cleanName,
            Email        = cleanEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role         = assignedRole,
            IsActive     = true
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("New user '{Email}' registered successfully with role '{Role}'.", user.Email, user.Role);

        var token     = _jwtService.GenerateToken(user);
        var expiresAt = _jwtService.GetExpirationUtc();

        return CreatedAtAction(nameof(Register), new AuthResponseDto(
            token, user.FullName, user.Email, user.Role, expiresAt
        ));
    }

    /// <summary>
    /// Authenticate credentials and issue a signed JWT token.
    /// Protected by brute-force rate limiting.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var cleanEmail = SanitizationHelper.NormalizeEmail(dto.Email);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == cleanEmail);

        // Constant-time protection against timing attacks: always perform BCrypt verify
        var passwordValid = user != null && BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);

        if (!passwordValid || user == null)
        {
            _logger.LogWarning("Failed login attempt for email: {Email}", cleanEmail);
            return Unauthorized(new { message = "Invalid email or password." });
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login rejected: Account for '{Email}' is deactivated.", cleanEmail);
            return Unauthorized(new { message = "This account has been deactivated. Please contact an administrator." });
        }

        _logger.LogInformation("User '{Email}' logged in successfully.", user.Email);

        var token     = _jwtService.GenerateToken(user);
        var expiresAt = _jwtService.GetExpirationUtc();

        return Ok(new AuthResponseDto(
            token, user.FullName, user.Email, user.Role, expiresAt
        ));
    }

    /// <summary>
    /// Password-less entry point for the public sandbox. Returns a JWT for one
    /// of the seeded demo accounts, flagged with the "demo" claim so the API
    /// can disable email delivery and restrict the reset endpoint.
    /// </summary>
    [HttpPost("demo")]
    [EnableRateLimiting("demo")]
    public async Task<IActionResult> DemoLogin([FromBody] DemoLoginDto dto)
    {
        if (!_demo.Enabled)
            return NotFound(new { message = "Demo mode is not enabled." });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var role = (dto.Role ?? "Admin").Trim();

        var email = role switch
        {
            "Admin"        => _demo.AdminEmail,
            "Doctor"       => _demo.DoctorEmail,
            "Receptionist" => _demo.ReceptionistEmail,
            _              => null
        };

        if (email is null)
            return BadRequest(new { message = "Role must be 'Admin', 'Doctor' or 'Receptionist'." });

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
        if (user is null)
        {
            _logger.LogError("Demo entry failed: seed account '{Email}' was not found.", email);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "The demo sandbox is not ready yet. Please try again in a moment." });
        }

        _logger.LogInformation("Demo session started as {Role} ({Email}).", role, email);

        var token     = _jwtService.GenerateToken(user, isDemo: true);
        var expiresAt = _jwtService.GetExpirationUtc();

        return Ok(new AuthResponseDto(
            token, user.FullName, user.Email, user.Role, expiresAt, IsDemo: true
        ));
    }
}
