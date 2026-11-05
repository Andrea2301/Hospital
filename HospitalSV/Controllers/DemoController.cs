using HospitalSV.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HospitalSV.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DemoController : ControllerBase
{
    private readonly DemoOptions _options;
    private readonly DemoState _state;
    private readonly DemoResetService _resetService;

    public DemoController(
        IOptions<DemoOptions> options,
        DemoState state,
        DemoResetService resetService)
    {
        _options      = options.Value;
        _state        = state;
        _resetService = resetService;
    }

    /// <summary>
    /// Public status used by the frontend to decide whether to show the
    /// sandbox entry points and the demo banner.
    /// </summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public IActionResult Status()
    {
        var (lastResetAt, cooldownUntil) = _state.Snapshot();
        var now = DateTime.UtcNow;

        return Ok(new
        {
            enabled                    = _options.Enabled,
            resetIntervalHours         = Math.Max(1, _options.ResetIntervalHours),
            manualResetCooldownMinutes = Math.Max(1, _options.ManualResetCooldownMinutes),
            lastResetAt,
            nextResetAt                = lastResetAt?.AddHours(Math.Max(1, _options.ResetIntervalHours)),
            cooldownUntil,
            serverTime                 = now
        });
    }

    /// <summary>
    /// Restores the sandbox to its seed state. Only a session issued by
    /// /api/auth/demo (claim demo=true) can call it.
    /// </summary>
    [HttpPost("reset")]
    [Authorize]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        if (!_options.Enabled)
            return NotFound(new { message = "Demo mode is not enabled." });

        if (!User.HasClaim("demo", "true"))
            return Forbid();

        var now = DateTime.UtcNow;
        var cooldown = TimeSpan.FromMinutes(Math.Max(1, _options.ManualResetCooldownMinutes));

        if (!_state.TryBeginManualReset(now, cooldown, out var cooldownUntil))
        {
            var retryAfter = Math.Max(1, (int)Math.Ceiling((cooldownUntil - now).TotalSeconds));
            Response.Headers.RetryAfter = retryAfter.ToString();

            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message           = $"The demo was reset recently. Try again in {retryAfter} seconds.",
                retryAfterSeconds = retryAfter
            });
        }

        try
        {
            await _resetService.ResetAsync(ct);
            _state.MarkReset(DateTime.UtcNow);
        }
        catch
        {
            _state.ReleaseManualReset();
            throw;
        }

        return Ok(new
        {
            message       = "Demo data restored. Reopening the sandbox…",
            resetAt       = DateTime.UtcNow,
            cooldownUntil
        });
    }
}
