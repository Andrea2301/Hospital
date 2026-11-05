using Microsoft.Extensions.Options;

namespace HospitalSV.Services;

/// <summary>
/// Resets the sandbox on a fixed interval while the API is running.
/// The first reset happens one interval after startup, so a fresh deploy
/// keeps the seed created during boot.
/// </summary>
public sealed class DemoResetHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DemoOptions _options;
    private readonly DemoState _state;
    private readonly ILogger<DemoResetHostedService> _logger;

    public DemoResetHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<DemoOptions> options,
        DemoState state,
        ILogger<DemoResetHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options      = options.Value;
        _state        = state;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        var interval = TimeSpan.FromHours(Math.Max(1, _options.ResetIntervalHours));
        _logger.LogInformation(
            "Demo sandbox scheduled reset enabled: every {Hours} hour(s).",
            interval.TotalHours);

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var reset = scope.ServiceProvider.GetRequiredService<DemoResetService>();

                    await reset.ResetAsync(stoppingToken);
                    _state.MarkReset(DateTime.UtcNow);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scheduled demo sandbox reset failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
