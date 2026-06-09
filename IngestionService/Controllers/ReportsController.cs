using Microsoft.AspNetCore.Mvc;
using Shared.Data;
using Shared.DTOs;
using Microsoft.EntityFrameworkCore;

namespace IngestionService.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>GET /api/reports/sensors – all sensors and their current status.</summary>
    [HttpGet("sensors")]
    public async Task<ActionResult<IEnumerable<SensorStatusDto>>> GetSensors()
    {
        var sensors = await _db.Sensors.ToListAsync();
        return Ok(sensors.Select(s => new SensorStatusDto
        {
            SensorId = s.Id,
            Status = s.Status,
            Quality = s.Quality,
            LastSeenAt = s.LastSeenAt,
            MinRange = s.MinRange,
            MaxRange = s.MaxRange
        }));
    }

    /// <summary>GET /api/reports/history/{sensorId} – raw measurements for a sensor.</summary>
    [HttpGet("history/{sensorId}")]
    public async Task<ActionResult<IEnumerable<MeasurementDto>>> GetHistory(string sensorId, [FromQuery] int limit = 100)
    {
        var measurements = await _db.Measurements
            .Where(m => m.SensorId == sensorId && !m.IsConsensus)
            .OrderByDescending(m => m.MeasuredAt)
            .Take(limit)
            .ToListAsync();

        return Ok(measurements.Select(m => new MeasurementDto
        {
            Id = m.Id,
            SensorId = m.SensorId,
            Value = m.Value,
            MeasuredAt = m.MeasuredAt,
            AlarmPriority = m.AlarmPriority,
            IsConsensus = m.IsConsensus,
            Quality = m.Quality
        }));
    }

    /// <summary>GET /api/reports/consensus – all computed consensus values.</summary>
    [HttpGet("consensus")]
    public async Task<ActionResult<IEnumerable<ConsensusValueDto>>> GetConsensus([FromQuery] int limit = 60)
    {
        var values = await _db.ConsensusValues
            .OrderByDescending(c => c.ComputedAt)
            .Take(limit)
            .ToListAsync();

        return Ok(values.Select(c => new ConsensusValueDto
        {
            Id = c.Id,
            Value = c.Value,
            ComputedAt = c.ComputedAt,
            PeriodStart = c.PeriodStart,
            PeriodEnd = c.PeriodEnd,
            ContributingSensors = c.ContributingSensors
        }));
    }
}
