namespace Shared.Enums;

public enum SensorQuality
{
    GOOD,
    BAD,
    UNCERTAIN
}

public enum AlarmPriority
{
    None = 0,
    Priority1 = 1,
    Priority2 = 2,
    Priority3 = 3
}

public enum SensorStatus
{
    Active,
    Inactive,
    Blocked
}
