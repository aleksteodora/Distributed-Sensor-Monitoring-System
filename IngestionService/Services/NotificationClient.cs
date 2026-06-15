using Shared.DTOs;
using Shared.Enums;

namespace IngestionService.Services;

/// <summary>
/// Typed HTTP client that forwards alarm events to NotificationService.
///
/// Intentionally fire-and-forget from the caller's perspective:
/// a NotificationService outage must never block or fail the ingestion pipeline.
/// Errors are logged but swallowed here.
/// </summary>
public class NotificationClient
{
    private readonly HttpClient _http;
    private readonly ILogger<NotificationClient> _logger;

    public NotificationClient(HttpClient http, ILogger<NotificationClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task SendAlarmAsync(string sensorId, double value,
                                     AlarmPriority priority, DateTimeOffset triggeredAt)
    {
        // AlarmEventDto field names match Shared.DTOs exactly:
        //   Priority  (not AlarmPriority — that's the enum type name)
        var dto = new AlarmEventDto
        {
            SensorId = sensorId,
            Value = value,
            Priority = priority,       // ← correct field name from AlarmEventDto
            TriggeredAt = triggeredAt,
        };

        try
        {
            var response = await _http.PostAsJsonAsync("/api/notifications/alarm", dto);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning(
                    "NotificationService returned {Status} for alarm from {SensorId}",
                    (int)response.StatusCode, sensorId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to forward alarm to NotificationService for sensor {SensorId}", sensorId);
        }
    }
}