using Shared.Enums;

namespace Shared.DTOs;

public class SensorStatusDto
{
    public string SensorId { get; set; } = string.Empty;
    public SensorStatus Status { get; set; }
    public SensorQuality Quality { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public double MinRange { get; set; }
    public double MaxRange { get; set; }
}

public class MeasurementDto
{
    public long Id { get; set; }
    public string SensorId { get; set; } = string.Empty;
    public double Value { get; set; }
    public DateTimeOffset MeasuredAt { get; set; }
    public AlarmPriority AlarmPriority { get; set; }
    public bool IsConsensus { get; set; }
    public SensorQuality Quality { get; set; }
}

public class ConsensusValueDto
{
    public long Id { get; set; }
    public double Value { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public int ContributingSensors { get; set; }
}
