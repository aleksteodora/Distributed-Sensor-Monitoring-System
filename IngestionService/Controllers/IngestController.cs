using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.DTOs;
using Shared.Enums;
using Shared.Models;
using IngestionService.Services;

namespace IngestionService.Controllers;

[ApiController]
[Route("api/ingest")]
public class IngestController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CryptoService _crypto;
    private readonly NotificationClient _notifier;
    private readonly BlockService _blockService;
    private readonly ILogger<IngestController> _logger;

    public IngestController(
        AppDbContext db,
        CryptoService crypto,
        NotificationClient notifier,
        BlockService blockService,
        ILogger<IngestController> logger)
    {
        _db = db;
        _crypto = crypto;
        _notifier = notifier;
        _blockService = blockService;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // POST /api/ingest
    //
    // Full pipeline:
    //   1. Look up sensor — register as Inactive if first contact
    //   2. Reject if sensor is Blocked
    //   3. Verify RSA-PSS signature
    //   4. Replay protection: timestamp window + MessageId monotonicity
    //   5. AES-256-CBC decrypt payload
    //   6. Persist Measurement to DB
    //   7. Update Sensor.LastSeenAt, Quality, LastMessageId
    //   8. Forward alarm to NotificationService (fire-and-forget)
    // -------------------------------------------------------------------------
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Ingest([FromBody] EncryptedMessageDto message)
    {
        // ?? 1. Sensor lookup / first-contact registration ?????????????????
        // Note: Sensor.Id is the string primary key (e.g. "SENSOR-01")
        var sensor = await _db.Sensors.FindAsync(message.SensorId);

        if (sensor == null)
        {
            // First time we hear from this sensor — register it.
            // FaultToleranceService will promote it to Active if a slot is free.
            sensor = new Sensor
            {
                Id = message.SensorId,
                Status = SensorStatus.Inactive,
                Quality = SensorQuality.UNCERTAIN,
                RegisteredAt = DateTimeOffset.UtcNow,
                LastSeenAt = null,
                LastMessageId = -1,   // any first MessageId (? 0) will be accepted
            };
            _db.Sensors.Add(sensor);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "First contact from sensor {SensorId} — registered as Inactive",
                message.SensorId);
        }

        // ?? 2. Blocked check ??????????????????????????????????????????????
        if (sensor.Status == SensorStatus.Blocked || _blockService.IsBlocked(message.SensorId))
        {
            _logger.LogWarning("Blocked sensor {SensorId} tried to send a message", message.SensorId);
            return StatusCode(StatusCodes.Status403Forbidden,
                $"Sensor {message.SensorId} is temporarily blocked.");
        }

        // ?? 3. Signature verification ?????????????????????????????????????
        if (!_crypto.VerifySignature(message))
        {
            _logger.LogWarning("Bad signature from sensor {SensorId}", message.SensorId);
            return Unauthorized("Signature verification failed.");
        }

        // ?? 4a. Replay protection — timestamp freshness ???????????????????
        // Accept messages no older than 30 s and no more than 5 s in the future
        // (to tolerate minor clock skew between sensor and server).
        var age = DateTimeOffset.UtcNow - message.SentAt.ToUniversalTime();
        if (age.TotalSeconds > 30 || age.TotalSeconds < -5)
        {
            _logger.LogWarning(
                "Replay rejected for {SensorId}: message age {Age:F1}s",
                message.SensorId, age.TotalSeconds);
            return BadRequest("Message timestamp is outside the acceptable window.");
        }

        // ?? 4b. Replay protection — MessageId monotonicity ????????????????
        // Sensor.LastMessageId is persisted in the DB so it survives server restarts.
        if (message.MessageId <= sensor.LastMessageId)
        {
            _logger.LogWarning(
                "Replay rejected for {SensorId}: MessageId {New} ? last accepted {Last}",
                message.SensorId, message.MessageId, sensor.LastMessageId);
            return BadRequest("MessageId is not greater than the last accepted value.");
        }

        // ?? 5. Decrypt payload ????????????????????????????????????????????
        SensorReadingDto reading;
        try
        {
            var json = _crypto.DecryptPayload(message.EncryptedPayload);
            reading = JsonSerializer.Deserialize<SensorReadingDto>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Deserialized payload was null.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Decryption/deserialization failed for sensor {SensorId}", message.SensorId);
            return BadRequest("Failed to decrypt or parse payload.");
        }

        // ?? 6. Persist measurement ????????????????????????????????????????
        var measurement = new Measurement
        {
            SensorId = message.SensorId,
            Value = reading.Value,
            MeasuredAt = reading.MeasuredAt,          // field name from SensorReadingDto
            ReceivedAt = DateTimeOffset.UtcNow,
            Quality = reading.Quality,
            AlarmPriority = reading.AlarmPriority,
            IsConsensus = false,
        };
        _db.Measurements.Add(measurement);

        // ?? 7. Update sensor state ????????????????????????????????????????
        sensor.LastSeenAt = DateTimeOffset.UtcNow;
        sensor.LastMessageId = message.MessageId;

        if (sensor.Quality != reading.Quality)
        {
            _logger.LogInformation(
                "Sensor {SensorId} quality: {Old} ? {New}",
                message.SensorId, sensor.Quality, reading.Quality);
            sensor.Quality = reading.Quality;
        }

        await _db.SaveChangesAsync();

        _logger.LogDebug(
            "Persisted measurement — sensor={SensorId} value={Value} alarm={Alarm}",
            message.SensorId, reading.Value, reading.AlarmPriority);

        // ?? 8. Forward alarm (fire-and-forget) ???????????????????????????
        if (reading.AlarmPriority != AlarmPriority.None)
        {
            _ = _notifier.SendAlarmAsync(
                message.SensorId,
                reading.Value,
                reading.AlarmPriority,
                reading.MeasuredAt);
        }

        return Accepted();
    }

    // -------------------------------------------------------------------------
    // POST /api/ingest/block/{sensorId}
    // Temporarily blocks a sensor for 30 seconds.
    // Used to test the fault-tolerance / spare-activation logic.
    // -------------------------------------------------------------------------
    [HttpPost("block/{sensorId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BlockSensor(string sensorId)
    {
        var exists = await _db.Sensors.AnyAsync(s => s.Id == sensorId);
        if (!exists)
            return NotFound($"Sensor '{sensorId}' not found.");

        await _blockService.BlockAsync(sensorId);

        return Ok(new
        {
            message = $"Sensor {sensorId} blocked for 30 seconds.",
            blockedAt = DateTimeOffset.UtcNow,
        });
    }
}