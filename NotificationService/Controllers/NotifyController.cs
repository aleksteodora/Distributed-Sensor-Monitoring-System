using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;
using Shared.DTOs;
using Shared.Enums;

namespace NotificationService.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotifyController : ControllerBase
{
    private readonly IHubContext<AlarmHub> _hub;
    private readonly ILogger<NotifyController> _logger;

    public NotifyController(IHubContext<AlarmHub> hub, ILogger<NotifyController> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    [HttpPost("alarm")]
    public async Task<IActionResult> NotifyAlarm([FromBody] AlarmEventDto alarm)
    {
        // Logovanje u boji prema prioritetu alarma
        LogAlarmToConsole(alarm);

        // SignalR broadcast svim konektovanim klijentima
        await _hub.Clients.All.SendAsync("AlarmTriggered", alarm);

        return Ok();
    }

    private void LogAlarmToConsole(AlarmEventDto alarm)
    {
        // Čuvamo originalne boje konzole
        var originalFg = Console.ForegroundColor;
        var originalBg = Console.BackgroundColor;

        (string label, ConsoleColor fg, ConsoleColor bg) style = alarm.Priority switch
        {
            AlarmPriority.Priority1 => ("⚠  PRIORITY 1", ConsoleColor.Black,  ConsoleColor.Yellow),
            AlarmPriority.Priority2 => ("🔶 PRIORITY 2", ConsoleColor.Black,  ConsoleColor.DarkYellow),
            AlarmPriority.Priority3 => ("🚨 PRIORITY 3", ConsoleColor.White,  ConsoleColor.Red),
            _                       => ("ℹ  NONE",       ConsoleColor.Gray,   ConsoleColor.Black),
        };

        Console.ForegroundColor = style.fg;
        Console.BackgroundColor = style.bg;

        Console.WriteLine(
            $"[{alarm.TriggeredAt:HH:mm:ss}] {style.label} | Sensor: {alarm.SensorId,-12} | Value: {alarm.Value,10:F4}");

        // Reset boja
        Console.ForegroundColor = originalFg;
        Console.BackgroundColor = originalBg;

        // Dodatni log kroz ILogger (bez boje, ide u Serilog/file sink ako postoji)
        _logger.LogWarning(
            "ALARM [{Priority}] sensor={SensorId} value={Value} at={TriggeredAt}",
            alarm.Priority, alarm.SensorId, alarm.Value, alarm.TriggeredAt);
    }
}