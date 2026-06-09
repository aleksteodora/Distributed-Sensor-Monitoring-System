using Shared.Enums;

namespace Shared.DTOs;

public class AlarmEventDto
{
    public string SensorId { get; set; } = string.Empty;
    public double Value { get; set; }
    public AlarmPriority Priority { get; set; }
    public DateTimeOffset TriggeredAt { get; set; }
}
