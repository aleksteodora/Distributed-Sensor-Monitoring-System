using System.Collections.Concurrent;
using System.Text.Json;
using IngestionService.Services;

namespace IngestionService.Middleware;

/// <summary>
/// Per-sensor rate limiter using a 1-second sliding window.
///
/// If any sensorId sends more than 10 requests within 1 second:
///   - Server responds immediately with 429 Too Many Requests.
///   - Sensor is blocked for 60 seconds via BlockService.
///
/// The middleware runs BEFORE routing so malicious traffic is dropped
/// as early as possible — the controller never even executes.
///
/// Only applies to POST /api/ingest. All other routes pass through.
/// </summary>
public class SensorRateLimitMiddleware
{
    private const int MaxRequestsPerSecond = 10;
    private const int BlockDurationSeconds = 60;

    // sensorId → timestamps of recent requests (sliding window)
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new();

    private readonly RequestDelegate _next;
    private readonly ILogger<SensorRateLimitMiddleware> _logger;

    public SensorRateLimitMiddleware(
        RequestDelegate next,
        ILogger<SensorRateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only rate-limit the ingest POST endpoint
        if (!context.Request.Path.StartsWithSegments("/api/ingest")
            || context.Request.Method != HttpMethods.Post
            || context.Request.Path.Value?.Contains("block") == true)
        {
            await _next(context);
            return;
        }

        // Enable buffering so we can peek at the body here and the
        // controller can still read it afterwards
        context.Request.EnableBuffering();

        string? sensorId = null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(
                context.Request.Body,
                cancellationToken: context.RequestAborted);

            doc.RootElement.TryGetProperty("sensorId", out var prop);
            sensorId = prop.GetString();
        }
        catch
        {
            // Malformed JSON — let the controller return the proper 400
        }
        finally
        {
            context.Request.Body.Position = 0; // rewind for the controller
        }

        if (sensorId == null)
        {
            await _next(context);
            return;
        }

        // Check if already blocked by BlockService
        var blockService = context.RequestServices.GetRequiredService<BlockService>();
        if (blockService.IsBlocked(sensorId))
        {
            _logger.LogWarning(
                "Rate-blocked sensor {SensorId} attempted request", sensorId);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync(
                $"Sensor {sensorId} is temporarily blocked due to rate limit violation.");
            return;
        }

        // Sliding window check
        var now = DateTime.UtcNow;
        var window = _windows.GetOrAdd(sensorId, _ => new Queue<DateTime>());

        bool limitExceeded;
        lock (window)
        {
            // Drop timestamps older than 1 second
            var cutoff = now.AddSeconds(-1);
            while (window.Count > 0 && window.Peek() < cutoff)
                window.Dequeue();

            window.Enqueue(now);
            limitExceeded = window.Count > MaxRequestsPerSecond;
        }

        if (limitExceeded)
        {
            _logger.LogWarning(
                "Sensor {SensorId} exceeded {Max} req/s — triggering block",
                sensorId, MaxRequestsPerSecond);

            // Block asynchronously — fire and forget so we can reply immediately
            _ = blockService.BlockAsync(sensorId);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = BlockDurationSeconds.ToString();
            await context.Response.WriteAsync(
                $"Rate limit exceeded. Sensor {sensorId} blocked for {BlockDurationSeconds}s.");
            return;
        }

        await _next(context);
    }
}

public static class RateLimitExtensions
{
    public static IApplicationBuilder UseSensorRateLimit(this IApplicationBuilder app)
        => app.UseMiddleware<SensorRateLimitMiddleware>();
}