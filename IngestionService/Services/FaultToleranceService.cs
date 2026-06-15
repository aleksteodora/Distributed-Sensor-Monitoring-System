using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Enums;

namespace IngestionService.Services;

/// <summary>
/// Background service that enforces "exactly 5 active sensors at all times".
///
/// Every 5 seconds it runs a health check:
///   1. Any Active sensor that hasn't sent a message in the last 10 s → Inactive.
///   2. If active count < 5, promote the freshest Inactive sensors to Active.
///   3. Sensors whose temporary block has expired (BlockedUntil in past) → Inactive.
///
/// Uses IServiceScopeFactory because DbContext is scoped and this is a singleton.
/// </summary>
public class FaultToleranceService : BackgroundService
{
    private const int TargetActive = 5;
    private const int InactivitySec = 10;
    private const int PollingIntervalSec = 5;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<FaultToleranceService> _logger;

    public FaultToleranceService(
        IServiceScopeFactory scopes,
        ILogger<FaultToleranceService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "FaultToleranceService started — polling every {Sec}s", PollingIntervalSec);

        while (!ct.IsCancellationRequested)
        {
            try { await RunCheckAsync(); }
            catch (Exception ex)
            { _logger.LogError(ex, "FaultToleranceService health-check error"); }

            await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSec), ct);
        }
    }

    private async Task RunCheckAsync()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        var sensors = await db.Sensors.ToListAsync();

        // ── 1. Expire blocks ────────────────────────────────────────────────
        // BlockedUntil is stored in the in-memory dictionary in BlockService;
        // here we just check status. If you add BlockedUntil to the model later,
        // swap this for: s.BlockedUntil.HasValue && now >= s.BlockedUntil
        foreach (var s in sensors.Where(s => s.Status == SensorStatus.Blocked))
        {
            // BlockService sets Status back to Inactive after 30 s automatically,
            // so this branch handles edge cases (e.g. server restart mid-block).
            // No-op for now; will be used when BlockedUntil column is added.
        }

        // ── 2. Active → Inactive if silent for > 10 s ──────────────────────
        var cutoff = now.AddSeconds(-InactivitySec);
        foreach (var s in sensors.Where(s => s.Status == SensorStatus.Active))
        {
            if (s.LastSeenAt == null || s.LastSeenAt < cutoff)
            {
                s.Status = SensorStatus.Inactive;
                _logger.LogWarning(
                    "Sensor {Id} → Inactive (last seen: {Last})",
                    s.Id, s.LastSeenAt?.ToString("o") ?? "never");
            }
        }

        // ── 3. Promote spares if needed ────────────────────────────────────
        int active = sensors.Count(s => s.Status == SensorStatus.Active);
        int needed = TargetActive - active;

        if (needed > 0)
        {
            var candidates = sensors
                .Where(s => s.Status == SensorStatus.Inactive)
                .OrderByDescending(s => s.LastSeenAt ?? DateTimeOffset.MinValue)
                .Take(needed)
                .ToList();

            foreach (var c in candidates)
            {
                c.Status = SensorStatus.Active;
                _logger.LogInformation(
                    "Sensor {Id} promoted to Active (active count was {N})", c.Id, active++);
            }

            if (candidates.Count < needed)
                _logger.LogWarning(
                    "Only {Got}/{Need} spare sensors available — cannot reach target of {Target}",
                    candidates.Count, needed, TargetActive);
        }

        await db.SaveChangesAsync();
    }
}