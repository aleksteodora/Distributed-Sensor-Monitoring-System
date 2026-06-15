using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Enums;

namespace IngestionService.Services;

/// <summary>
/// Manages temporary 30-second sensor blocks for fault-tolerance testing.
///
/// Why a separate service instead of a DB column?
///   The Sensor model doesn't have a BlockedUntil column yet.
///   This service tracks expiry in memory and resets the DB status when the
///   block expires. If you later add BlockedUntil to the Sensor model, you
///   can move the expiry logic to FaultToleranceService and retire this class.
/// </summary>
public class BlockService : BackgroundService
{
    private const int BlockDurationSec = 30;

    // sensorId → when the block expires
    private static readonly ConcurrentDictionary<string, DateTimeOffset> _blocks = new();

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BlockService> _logger;

    public BlockService(IServiceScopeFactory scopes, ILogger<BlockService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    // ── Called by IngestController.BlockSensor ──────────────────────────────
    public async Task BlockAsync(string sensorId)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(BlockDurationSec);
        _blocks[sensorId] = until;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sensor = await db.Sensors.FindAsync(sensorId);
        if (sensor != null)
        {
            sensor.Status = SensorStatus.Blocked;
            await db.SaveChangesAsync();
        }

        _logger.LogInformation("Sensor {Id} blocked until {Until}", sensorId, until);
    }

    public bool IsBlocked(string sensorId)
        => _blocks.TryGetValue(sensorId, out var until) && DateTimeOffset.UtcNow < until;

    // ── Background loop: unblock sensors whose timer has expired ───────────
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var expired = _blocks.Where(kv => now >= kv.Value).Select(kv => kv.Key).ToList();

            if (expired.Count > 0)
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                foreach (var id in expired)
                {
                    _blocks.TryRemove(id, out _);

                    var sensor = await db.Sensors.FindAsync(id);
                    if (sensor?.Status == SensorStatus.Blocked)
                    {
                        sensor.Status = SensorStatus.Inactive;
                        _logger.LogInformation("Sensor {Id} block expired → Inactive", id);
                    }
                }

                await db.SaveChangesAsync();
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
}