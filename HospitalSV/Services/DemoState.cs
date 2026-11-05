namespace HospitalSV.Services;

/// <summary>
/// In-memory, process-wide state for the demo sandbox: when it was last
/// reset and the cooldown that protects the manual reset endpoint.
/// A single API instance is assumed (the deployment keeps one task).
/// </summary>
public sealed class DemoState
{
    private readonly object _gate = new();
    private DateTime? _lastResetAt;
    private DateTime? _cooldownUntil;

    public (DateTime? LastResetAt, DateTime? CooldownUntil) Snapshot()
    {
        lock (_gate)
        {
            return (_lastResetAt, _cooldownUntil);
        }
    }

    /// <summary>
    /// Reserves a manual reset. Returns false (and the active cooldown) when
    /// another reset happened too recently.
    /// </summary>
    public bool TryBeginManualReset(DateTime now, TimeSpan cooldown, out DateTime cooldownUntil)
    {
        lock (_gate)
        {
            if (_cooldownUntil is { } active && active > now)
            {
                cooldownUntil = active;
                return false;
            }

            cooldownUntil = now.Add(cooldown);
            _cooldownUntil = cooldownUntil;
            return true;
        }
    }

    /// <summary>Releases a failed manual reset so the visitor can retry.</summary>
    public void ReleaseManualReset()
    {
        lock (_gate)
        {
            _cooldownUntil = null;
        }
    }

    public void MarkReset(DateTime at)
    {
        lock (_gate)
        {
            _lastResetAt = at;
        }
    }
}
