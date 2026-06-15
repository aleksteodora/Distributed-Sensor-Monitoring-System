using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.DTOs;
using Shared.Enums;

namespace IngestionService.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db) => _db = db;

    // -------------------------------------------------------------------------
    // GET /api/reports/sensors
    // All sensors the server has ever seen, with their current status.
    // -------------------------------------------------------------------------
    [HttpGet("sensors")]
    [ProducesResponseType(typeof(IEnumerable<SensorStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSensors()
    {
        var sensors = await _db.Sensors
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new SensorStatusDto
            {
                SensorId = s.Id,          // Sensor.Id is the string PK ("SENSOR-01")
                Status = s.Status,
                Quality = s.Quality,
                LastSeenAt = s.LastSeenAt,
                MinRange = s.MinRange,
                MaxRange = s.MaxRange,
            })
            .ToListAsync();

        return Ok(sensors);
    }

    // -------------------------------------------------------------------------
    // GET /api/reports/sensors/{sensorId}
    // Single sensor detail.
    // -------------------------------------------------------------------------
    [HttpGet("sensors/{sensorId}")]
    [ProducesResponseType(typeof(SensorStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSensor(string sensorId)
    {
        var s = await _db.Sensors.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sensorId);

        if (s == null) return NotFound();

        return Ok(new SensorStatusDto
        {
            SensorId = s.Id,
            Status = s.Status,
            Quality = s.Quality,
            LastSeenAt = s.LastSeenAt,
            MinRange = s.MinRange,
            MaxRange = s.MaxRange,
        });
    }

    // -------------------------------------------------------------------------
    // GET /api/reports/history/{sensorId}?limit=100
    // Raw (non-consensus) measurements for a sensor, newest first.
    // -------------------------------------------------------------------------
    [HttpGet("history/{sensorId}")]
    [ProducesResponseType(typeof(IEnumerable<MeasurementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(string sensorId, [FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 1000);

        var measurements = await _db.Measurements
            .AsNoTracking()
            .Where(m => m.SensorId == sensorId && !m.IsConsensus)
            .OrderByDescending(m => m.MeasuredAt)
            .Take(limit)
            .Select(m => new MeasurementDto
            {
                Id = m.Id,
                SensorId = m.SensorId,
                Value = m.Value,
                MeasuredAt = m.MeasuredAt,
                AlarmPriority = m.AlarmPriority,
                IsConsensus = m.IsConsensus,
                Quality = m.Quality,
            })
            .ToListAsync();

        return Ok(measurements);
    }

    // -------------------------------------------------------------------------
    // GET /api/reports/consensus?limit=60
    // Most recent consensus values, newest first.
    // -------------------------------------------------------------------------
    [HttpGet("consensus")]
    [ProducesResponseType(typeof(IEnumerable<ConsensusValueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConsensus([FromQuery] int limit = 60)
    {
        limit = Math.Clamp(limit, 1, 1440);

        var values = await _db.ConsensusValues
            .AsNoTracking()
            .OrderByDescending(c => c.ComputedAt)
            .Take(limit)
            .Select(c => new ConsensusValueDto
            {
                Id = c.Id,
                Value = c.Value,
                ComputedAt = c.ComputedAt,
                PeriodStart = c.PeriodStart,
                PeriodEnd = c.PeriodEnd,
                ContributingSensors = c.ContributingSensors,
            })
            .ToListAsync();

        return Ok(values);
    }

    // -------------------------------------------------------------------------
    // GET /api/reports/alarms?limit=50
    // Most recent measurements where an alarm was triggered, across all sensors.
    // -------------------------------------------------------------------------
    [HttpGet("alarms")]
    [ProducesResponseType(typeof(IEnumerable<MeasurementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAlarms([FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 500);

        var alarms = await _db.Measurements
            .AsNoTracking()
            .Where(m => m.AlarmPriority != AlarmPriority.None)
            .OrderByDescending(m => m.MeasuredAt)
            .Take(limit)
            .Select(m => new MeasurementDto
            {
                Id = m.Id,
                SensorId = m.SensorId,
                Value = m.Value,
                MeasuredAt = m.MeasuredAt,
                AlarmPriority = m.AlarmPriority,
                IsConsensus = m.IsConsensus,
                Quality = m.Quality,
            })
            .ToListAsync();

        return Ok(alarms);
    }
}