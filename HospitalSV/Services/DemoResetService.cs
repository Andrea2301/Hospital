using HospitalSV.Data;
using Microsoft.EntityFrameworkCore;

namespace HospitalSV.Services;

/// <summary>
/// Wipes and reseeds the sandbox inside a single transaction, so visitors
/// never see a half-restored clinic.
/// </summary>
public class DemoResetService
{
    private readonly AppDbContext _context;
    private readonly ILogger<DemoResetService> _logger;

    public DemoResetService(AppDbContext context, ILogger<DemoResetService> logger)
    {
        _context = context;
        _logger  = logger;
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        await DemoSeeder.ResetAsync(_context, ct);

        await transaction.CommitAsync(ct);

        _logger.LogInformation("Demo sandbox data restored.");
    }
}
