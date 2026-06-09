var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();

var app = builder.Build();

app.MapControllers();
app.MapHub<NotificationService.Hubs.AlarmHub>("/hubs/alarms");

app.Run();
