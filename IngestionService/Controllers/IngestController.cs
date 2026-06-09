using Microsoft.AspNetCore.Mvc;
using Shared.DTOs;

namespace IngestionService.Controllers;

[ApiController]
[Route("api/ingest")]
public class IngestController : ControllerBase
{
    private readonly ILogger<IngestController> _logger;

    public IngestController(ILogger<IngestController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Steps (TODO Student 2):
    ///   1. Validate SentAt is within acceptable window (replay protection)
    ///   2. Validate MessageId > last received MessageId for this sensor (replay protection)
    ///   3. Verify signature using sensor's public key
    ///   4. Decrypt EncryptedPayload with AES
    ///   5. Persist Measurement to DB
    ///   6. If AlarmPriority > 0, POST to NotificationService
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Ingest([FromBody] EncryptedMessageDto message)
    {
        _logger.LogInformation("Received message from sensor {SensorId}, MessageId={MessageId}",
            message.SensorId, message.MessageId);

        // TODO: implement full pipeline
        return Accepted();
    }
}
