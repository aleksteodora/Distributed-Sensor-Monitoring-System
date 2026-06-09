using System.Net.Http.Json;
using Shared.Enums;

// TODO (Student 1): load sensor configs from args or config file
// For now, spin up 5 sensors with hardcoded values to verify connectivity

var sensors = new[]
{
    new SensorConfig("SENSOR-01", 200, 400, SensorQuality.GOOD, Threshold1: 350, Threshold2: 370, Threshold3: 390),
    new SensorConfig("SENSOR-02", 200, 400, SensorQuality.GOOD, Threshold1: 350, Threshold2: 370, Threshold3: 390),
    new SensorConfig("SENSOR-03", 200, 400, SensorQuality.GOOD, Threshold1: 350, Threshold2: 370, Threshold3: 390),
    new SensorConfig("SENSOR-04", 200, 400, SensorQuality.GOOD, Threshold1: 350, Threshold2: 370, Threshold3: 390),
    new SensorConfig("SENSOR-05", 200, 400, SensorQuality.GOOD, Threshold1: 350, Threshold2: 370, Threshold3: 390),
};

var ingressUrl = args.Length > 0 ? args[0] : "http://localhost:5000";

Console.WriteLine($"Starting 5 sensors → {ingressUrl}");

var tasks = sensors.Select(s => SensorLoop(s, ingressUrl, CancellationToken.None));
await Task.WhenAll(tasks);

static async Task SensorLoop(SensorConfig config, string ingressUrl, CancellationToken ct)
{
    var rng = new Random();
    long messageId = 0;
    using var http = new HttpClient { BaseAddress = new Uri(ingressUrl) };

    while (!ct.IsCancellationRequested)
    {
        var intervalMs = rng.Next(1000, 10001); // 1-10 seconds
        await Task.Delay(intervalMs, ct);

        var value = rng.NextDouble() * (config.MaxRange - config.MinRange) + config.MinRange;
        var priority = DetermineAlarmPriority(value, config);

        PrintToConsole(config.Id, value, priority);

        // TODO (Student 1): encrypt + sign the payload before sending
        var payload = new
        {
            SensorId = config.Id,
            Value = value,
            MeasuredAt = DateTimeOffset.UtcNow,
            Quality = config.Quality.ToString(),
            AlarmPriority = (int)priority,
            MessageId = ++messageId
        };

        // Temporary: send plain JSON until encryption is implemented
        try
        {
            var response = await http.PostAsJsonAsync("/api/ingest", new
            {
                SensorId = config.Id,
                EncryptedPayload = System.Text.Json.JsonSerializer.Serialize(payload),
                Signature = "TODO",
                MessageId = messageId,
                SentAt = DateTimeOffset.UtcNow
            });

            if (!response.IsSuccessStatusCode)
                Console.WriteLine($"[{config.Id}] Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{config.Id}] Send failed: {ex.Message}");
        }
    }
}

static AlarmPriority DetermineAlarmPriority(double value, SensorConfig config)
{
    if (config.Threshold3.HasValue && value >= config.Threshold3) return AlarmPriority.Priority3;
    if (config.Threshold2.HasValue && value >= config.Threshold2) return AlarmPriority.Priority2;
    if (config.Threshold1.HasValue && value >= config.Threshold1) return AlarmPriority.Priority1;
    return AlarmPriority.None;
}

static void PrintToConsole(string sensorId, double value, AlarmPriority priority)
{
    var color = priority switch
    {
        AlarmPriority.Priority1 => ConsoleColor.Yellow,
        AlarmPriority.Priority2 => ConsoleColor.DarkYellow,  // closest to orange
        AlarmPriority.Priority3 => ConsoleColor.Red,
        _ => ConsoleColor.Gray
    };

    Console.ForegroundColor = color;
    Console.WriteLine($"[{sensorId}] {DateTimeOffset.UtcNow:HH:mm:ss} → {value:F2}°  alarm={priority}");
    Console.ResetColor();
}

record SensorConfig(
    string Id,
    double MinRange,
    double MaxRange,
    SensorQuality Quality,
    double? Threshold1,
    double? Threshold2,
    double? Threshold3
);
