using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;
using Shared.DTOs;

namespace NotificationService.Controllers;

[ApiController]
[Route("api/notify")]
public class NotifyController : ControllerBase
{
    private readonly IHubContext<AlarmHub> _hub;
    private readonly ILogger<NotifyController> _logger;

    public NotifyController(IHubContext<AlarmHub> hub, ILogger<NotifyController> logger)
    {
        _hub = hub;
        _logger = logger;
    }
    
    // TODO (Student 3): secure this endpoint so only IngestionService can call it.
    [HttpPost("alarm")]
    public async Task<IActionResult> NotifyAlarm([FromBody] AlarmEventDto alarm)
    {
        _logger.LogWarning("ALARM from {SensorId}: value={Value}, priority={Priority}",
            alarm.SensorId, alarm.Value, alarm.Priority);

        await _hub.Clients.All.SendAsync("AlarmTriggered", alarm);
        return Ok();
    }
}
