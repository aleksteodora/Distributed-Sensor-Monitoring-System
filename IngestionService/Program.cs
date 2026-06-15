using Microsoft.EntityFrameworkCore;
using Shared.Data;
using IngestionService.Services;
using IngestionService.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Crypto (singleton — stateless, just reads config) ─────────────────────────
builder.Services.Configure<CryptoOptions>(
    builder.Configuration.GetSection(CryptoOptions.Section));
builder.Services.AddSingleton<CryptoService>();

// ── Block service (singleton — holds in-memory block state) ───────────────────
// Registered as both IHostedService (for the background unblock loop)
// and a directly-resolvable singleton so controllers can call BlockAsync().
builder.Services.AddSingleton<BlockService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BlockService>());

// ── Fault tolerance (background service) ──────────────────────────────────────
builder.Services.AddHostedService<FaultToleranceService>();

// ── Typed HTTP client → NotificationService ───────────────────────────────────
builder.Services.AddHttpClient<NotificationClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Services:NotificationService"]
        ?? "http://notification-service:5002");
    client.Timeout = TimeSpan.FromSeconds(5);
});

// ── Controllers + Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "IngestionService", Version = "v1" }));

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// ── Auto-migrate on startup ───────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Rate limiting runs BEFORE routing — malicious traffic never reaches controllers
app.UseSensorRateLimit();

app.MapControllers();

app.Run();