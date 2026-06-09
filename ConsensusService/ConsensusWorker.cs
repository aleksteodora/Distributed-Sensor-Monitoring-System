using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Enums;
using Shared.Models;

namespace ConsensusService;

// TODO (Student 3): implement full BFT consensus algorithm.
public class ConsensusWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConsensusWorker> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public ConsensusWorker(IServiceScopeFactory scopeFactory, ILogger<ConsensusWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ConsensusWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            await ComputeConsensusAsync(stoppingToken);
        }
    }

    private async Task ComputeConsensusAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var periodEnd = DateTimeOffset.UtcNow;
        var periodStart = periodEnd - Interval;

        // Only use GOOD quality, non-consensus raw readings
        var readings = await db.Measurements
            .Where(m => !m.IsConsensus
                     && m.Quality == SensorQuality.GOOD
                     && m.MeasuredAt >= periodStart
                     && m.MeasuredAt < periodEnd)
            .ToListAsync(ct);

        if (readings.Count == 0)
        {
            _logger.LogWarning("No GOOD readings in period {Start} – {End}", periodStart, periodEnd);
            return;
        }

        // TODO (Student 3): replace with proper simplified BFT algorithm
        double consensusValue = ComputeSimpleConsensus(readings.Select(r => r.Value).ToList());

        var entry = new ConsensusValue
        {
            Value = consensusValue,
            ComputedAt = DateTimeOffset.UtcNow,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            ContributingSensors = readings.Select(r => r.SensorId).Distinct().Count()
        };

        db.ConsensusValues.Add(entry);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Consensus for {Start}–{End}: {Value} (from {N} sensors)",
            periodStart, periodEnd, consensusValue, entry.ContributingSensors);
    }
    
    private static double ComputeSimpleConsensus(List<double> values)
    {
        values.Sort();
        int trim = (int)(values.Count * 0.2);
        var trimmed = values.Skip(trim).Take(values.Count - 2 * trim).ToList();
        return trimmed.Count > 0 ? trimmed.Average() : values.Average();
    }
}
