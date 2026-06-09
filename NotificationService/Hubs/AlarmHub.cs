using Microsoft.AspNetCore.SignalR;

namespace NotificationService.Hubs;

// TODO (Student 3): add authentication if needed.
public class AlarmHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", "You are connected to the alarm hub.");
        await base.OnConnectedAsync();
    }
}
