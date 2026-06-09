using Microsoft.EntityFrameworkCore;
using Shared.Data;
using ConsensusService;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHostedService<ConsensusWorker>();

var host = builder.Build();
host.Run();
