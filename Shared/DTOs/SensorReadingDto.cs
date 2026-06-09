using Shared.Enums;

namespace Shared.DTOs;

public class SensorReadingDto
{
    public string SensorId { get; set; } = string.Empty;
    public double Value { get; set; }
    public DateTimeOffset MeasuredAt { get; set; }
    public SensorQuality Quality { get; set; }
    public AlarmPriority AlarmPriority { get; set; }
    public long MessageId { get; set; }
}
