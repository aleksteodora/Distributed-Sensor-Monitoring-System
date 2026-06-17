using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Enums;
using Shared.Models;

namespace ConsensusService;

public class ConsensusWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConsensusWorker> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    // Outlier detection threshold: senzori čija median vrednost odstupa
    // više od 2 standardne devijacije od grupnog mediana -> označi kao BAD
    private const double OutlierSigmaThreshold = 2.0;

    // BFT zahtev: konsenzus je validan samo ako ima dovoljno "poštenih" senzora.
    // Sa f faulty senzora, treba >= 2f+1 ukupno. Ovde koristimo blažu varijantu:
    // odbacujemo outliere i prihvatamo konsenzus ako ostane bar 1 senzor.
    private const int MinContributingSensors = 1;

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

        // Čitamo samo GOOD merenja koja nisu već konsenzus vrednosti
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

        // ── Korak 1: grupišemo po senzoru, uzimamo median ──────────────────────
        var sensorMedians = readings
            .GroupBy(m => m.SensorId)
            .Select(g =>
            {
                var sorted = g.Select(m => m.Value).OrderBy(v => v).ToList();
                double median = ComputeMedian(sorted);
                return (SensorId: g.Key, Median: median, Count: g.Count());
            })
            .ToList();

        _logger.LogInformation("BFT round: {N} sensors participated in period {Start}–{End}",
            sensorMedians.Count, periodStart, periodEnd);

        foreach (var s in sensorMedians)
            _logger.LogInformation("  Sensor {Id}: median={Median:F4} ({Count} readings)",
                s.SensorId, s.Median, s.Count);

        // ── Korak 2: detektujemo outliere (>2σ od grupe) ────────────────────────
        var allMedians = sensorMedians.Select(s => s.Median).ToList();
        double groupMedian = ComputeMedian(allMedians.OrderBy(v => v).ToList());
        double sigma = ComputeStdDev(allMedians, groupMedian);

        var honest = new List<(string SensorId, double Median)>();
        var faulty = new List<(string SensorId, double Median, double Deviation)>();

        foreach (var s in sensorMedians)
        {
            double deviation = Math.Abs(s.Median - groupMedian);
            if (sigma > 0 && deviation > OutlierSigmaThreshold * sigma)
                faulty.Add((s.SensorId, s.Median, deviation));
            else
                honest.Add((s.SensorId, s.Median));
        }

        if (faulty.Count > 0)
        {
            _logger.LogWarning("BFT: {N} outlier sensor(s) excluded from consensus (threshold: {T}σ = {V:F4}):",
                faulty.Count, OutlierSigmaThreshold, OutlierSigmaThreshold * sigma);

            foreach (var f in faulty)
                _logger.LogWarning("  ⚠ Outlier: {Id} median={Median:F4} deviation={Dev:F4}",
                    f.SensorId, f.Median, f.Deviation);

            // Označi merenja tih senzora u ovom periodu kao BAD u bazi
            var faultySensorIds = faulty.Select(f => f.SensorId).ToHashSet();
            var faultyMeasurements = readings
                .Where(m => faultySensorIds.Contains(m.SensorId))
                .ToList();

            foreach (var m in faultyMeasurements)
                m.Quality = SensorQuality.BAD;
        }

        // ── Korak 3: konsenzus vrednost = prosek "poštenih" mediana ─────────────
        if (honest.Count < MinContributingSensors)
        {
            _logger.LogError(
                "BFT: Not enough honest sensors ({N}) to reach consensus. Skipping period.",
                honest.Count);
            await db.SaveChangesAsync(ct); // ipak sačuvaj BAD oznake
            return;
        }

        double consensusValue = honest.Select(s => s.Median).Average();

        _logger.LogInformation(
            "BFT consensus: {Value:F4} (from {Honest} honest sensor(s), {Faulty} excluded)",
            consensusValue, honest.Count, faulty.Count);

        // ── Korak 4: upisujemo ConsensusValue i jedno Measurement sa IsConsensus=true ──
        var entry = new ConsensusValue
        {
            Value = consensusValue,
            ComputedAt = DateTimeOffset.UtcNow,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            ContributingSensors = honest.Count
        };
        db.ConsensusValues.Add(entry);

        // Opcionalno: upiši konsenzus i kao Measurement za lakši prikaz u reports
        // Koristimo SensorId prvog poštenog senzora kao "vlasnika" zapisa
        if (honest.Count > 0)
        {
            var consensusMeasurement = new Measurement
            {
                SensorId = honest[0].SensorId,
                Value = consensusValue,
                MeasuredAt = periodEnd,
                ReceivedAt = DateTimeOffset.UtcNow,
                Quality = SensorQuality.GOOD,
                AlarmPriority = AlarmPriority.None,
                IsConsensus = true
            };
            db.Measurements.Add(consensusMeasurement);
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Consensus saved for {Start}–{End}: {Value:F4} ({N} sensors)",
            periodStart, periodEnd, consensusValue, honest.Count);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    // Median pretpostavlja da je lista već sortirana.
    private static double ComputeMedian(List<double> sorted)
    {
        int n = sorted.Count;
        if (n == 0) return 0;
        if (n % 2 == 1) return sorted[n / 2];
        return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private static double ComputeStdDev(List<double> values, double mean)
    {
        if (values.Count < 2) return 0;
        double variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Count;
        return Math.Sqrt(variance);
    }
}