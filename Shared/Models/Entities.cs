using Shared.Enums;

namespace Shared.Models;

public class Sensor
{
    public string Id { get; set; } = string.Empty;           // e.g. "SENSOR-01"
    public double MinRange { get; set; }
    public double MaxRange { get; set; }
    public SensorQuality Quality { get; set; }
    public SensorStatus Status { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }

    // Alarm thresholds
    public double? AlarmThreshold1 { get; set; }
    public double? AlarmThreshold2 { get; set; }
    public double? AlarmThreshold3 { get; set; }

    // Replay-attack protection: last accepted MessageId per sensor
    public long LastMessageId { get; set; }

    public ICollection<Measurement> Measurements { get; set; } = new List<Measurement>();
}

public class Measurement
{
    public long Id { get; set; }
    public string SensorId { get; set; } = string.Empty;
    public Sensor Sensor { get; set; } = null!;
    public double Value { get; set; }
    public DateTimeOffset MeasuredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public SensorQuality Quality { get; set; }
    public AlarmPriority AlarmPriority { get; set; }
    public bool IsConsensus { get; set; }
}

public class ConsensusValue
{
    public long Id { get; set; }
    public double Value { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public int ContributingSensors { get; set; }
}
