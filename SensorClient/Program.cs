using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Shared.Crypto;
using Shared.DTOs;
using Shared.Enums;


var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var aesKey = Convert.FromBase64String(config["Crypto:AesKey"]!);

var sensors = new[]
{
    new SensorConfig("SENSOR-01", 200, 400, SensorQuality.GOOD, Threshold1: 350, Threshold2: 370, Threshold3: 390, Malicious: MaliciousBehavior.None),
    new SensorConfig("SENSOR-02", 180, 380, SensorQuality.GOOD, Threshold1: 330, Threshold2: 355, Threshold3: 375, Malicious: MaliciousBehavior.None),
    new SensorConfig("SENSOR-03", 220, 410, SensorQuality.GOOD, Threshold1: 360, Threshold2: 380, Threshold3: 400, Malicious: MaliciousBehavior.None),
    new SensorConfig("SENSOR-04", 200, 400, SensorQuality.UNCERTAIN, Threshold1: 350, Threshold2: 370, Threshold3: 390, Malicious: MaliciousBehavior.DelaysAndBadData),
    new SensorConfig("SENSOR-05", 210, 395, SensorQuality.GOOD, Threshold1: 345, Threshold2: 365, Threshold3: 385, Malicious: MaliciousBehavior.None),
};

var ingressUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
Console.WriteLine($"Starting {sensors.Length} sensors → {ingressUrl}");

// Deljena mapa- koji senzori su trenutno blokirani za testiranje i do kada
var blockedUntil = new Dictionary<string, DateTimeOffset>();
var blockLock = new object();

// Pozadinski task koji cita komande sa konzole, ne blokira slanje
var consoleTask = Task.Run(() => ReadConsoleCommands(sensors, blockedUntil, blockLock));

var tasks = sensors.Select(s => SensorLoop(s, ingressUrl, aesKey, config, blockedUntil, blockLock, CancellationToken.None));
await Task.WhenAll(tasks.Append(consoleTask));

static void ReadConsoleCommands(SensorConfig[] sensors, Dictionary<string, DateTimeOffset> blockedUntil, object blockLock)
{
    Console.WriteLine();
    Console.WriteLine("Komande: ukucaj ID senzora (npr. SENSOR-03) da ga blokiraš na 30s ili 'list' za spisak senzora.");
    while (true)
    {
        var input = Console.ReadLine()?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(input)) continue;

        if (input == "LIST")
        {
            Console.WriteLine("Senzori: " + string.Join(", ", sensors.Select(s => s.Id)));
            continue;
        }

        if (sensors.Any(s => s.Id == input))
        {
            lock (blockLock)
            {
                blockedUntil[input] = DateTimeOffset.UtcNow.AddSeconds(30);
            }
            Console.WriteLine($"[{input}] blokiran na 30 sekundi (test fault tolerance).");
        }
        else
        {
            Console.WriteLine($"Nepoznat senzor '{input}'. Ukucaj 'list' za spisak.");
        }
    }
}

static async Task SensorLoop(
    SensorConfig config,
    string ingressUrl,
    byte[] aesKey,
    IConfiguration appConfig,
    Dictionary<string, DateTimeOffset> blockedUntil,
    object blockLock,
    CancellationToken ct)
{
    var rng = new Random();
    long messageId = 0;
    using var http = new HttpClient { BaseAddress = new Uri(ingressUrl) };

    var privateKeyBase64 = appConfig[$"Crypto:SensorKeys:{config.Id}"]!;
    using var privateKey = CryptoHelper.LoadPrivateKey(privateKeyBase64);

    while (!ct.IsCancellationRequested)
    {
        var intervalMs = rng.Next(1000, 10001); // 1-10 sekundi
        await Task.Delay(intervalMs, ct);

        // Provera da li je senzor privremeno blokiran (test fault tolerance)
        bool isBlocked;
        lock (blockLock)
        {
            isBlocked = blockedUntil.TryGetValue(config.Id, out var until) && DateTimeOffset.UtcNow < until;
        }
        if (isBlocked)
        {
            continue; // senzor "ne odgovara", server bi trebalo da ga proglasi neaktivnim nakon 10s
        }

        // Maliciozno ponasanje, samo ako je konfigurisano za ovaj senzor
        var quality = config.Quality;
        var value = rng.NextDouble() * (config.MaxRange - config.MinRange) + config.MinRange;

        if (config.Malicious == MaliciousBehavior.DelaysAndBadData)
        {
            // Povremeno: dodatno kasnjenje
            if (rng.NextDouble() < 0.3)
            {
                await Task.Delay(rng.Next(3000, 8000), ct);
            }

            // Povremeno: nevalidan/neispravan podatak (van opsega, ekstreman)
            if (rng.NextDouble() < 0.4)
            {
                value = rng.NextDouble() < 0.5
                    ? config.MaxRange + rng.Next(50, 500)   // ekstremno visoka vrednost
                    : -1 * rng.Next(0, 500);                 // negativna/nelogicna vrednost
                quality = SensorQuality.BAD;
            }
        }

        var priority = DetermineAlarmPriority(value, config);
        PrintToConsole(config.Id, value, priority);

        var reading = new SensorReadingDto
        {
            SensorId = config.Id,
            Value = value,
            MeasuredAt = DateTimeOffset.UtcNow,
            Quality = quality,
            AlarmPriority = priority,
            MessageId = ++messageId
        };

        var json = JsonSerializer.Serialize(reading);
        var encryptedPayload = CryptoHelper.EncryptAes(json, aesKey);
        var sentAt = DateTimeOffset.UtcNow;
        var signingInput = CryptoHelper.BuildSigningInput(config.Id, reading.MessageId, sentAt, encryptedPayload);
        var signature = CryptoHelper.Sign(signingInput, privateKey);

        var message = new EncryptedMessageDto
        {
            SensorId = config.Id,
            EncryptedPayload = encryptedPayload,
            Signature = signature,
            MessageId = reading.MessageId,
            SentAt = sentAt
        };

        try
        {
            var response = await http.PostAsJsonAsync("/api/ingest", message, ct);
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
        AlarmPriority.Priority2 => ConsoleColor.DarkYellow, // najblize narandzastoj
        AlarmPriority.Priority3 => ConsoleColor.Red,
        _ => ConsoleColor.Gray
    };
    Console.ForegroundColor = color;
    Console.WriteLine($"[{sensorId}] {DateTimeOffset.UtcNow:HH:mm:ss} → {value:F2}°  alarm={priority}");
    Console.ResetColor();
}

enum MaliciousBehavior
{
    None,
    DelaysAndBadData
}

record SensorConfig(
    string Id,
    double MinRange,
    double MaxRange,
    SensorQuality Quality,
    double? Threshold1,
    double? Threshold2,
    double? Threshold3,
    MaliciousBehavior Malicious = MaliciousBehavior.None
);