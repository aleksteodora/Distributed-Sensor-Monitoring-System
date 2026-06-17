using Microsoft.AspNetCore.SignalR;

namespace NotificationService.Hubs;

public class AlarmHub : Hub
{
    private readonly ILogger<AlarmHub> _logger;

    // Statički brojač aktivnih konekcija (thread-safe)
    private static int _connectionCount;

    public AlarmHub(ILogger<AlarmHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        int count = Interlocked.Increment(ref _connectionCount);
        _logger.LogInformation("SignalR client connected: {ConnectionId} (total: {Count})",
            Context.ConnectionId, count);

        await Clients.Caller.SendAsync("Connected", new
        {
            Message = "Connected to SNUS Alarm Hub.",
            ConnectionId = Context.ConnectionId,
            ActiveClients = count
        });

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        int count = Interlocked.Decrement(ref _connectionCount);
        _logger.LogInformation("SignalR client disconnected: {ConnectionId} (total: {Count})",
            Context.ConnectionId, count);

        await base.OnDisconnectedAsync(exception);
    }
}