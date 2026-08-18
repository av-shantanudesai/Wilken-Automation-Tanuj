using System.Text.Json.Serialization;
using WilkenAutomation.Api.Hubs;
using WilkenAutomation.Api.Services;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Services;
using WilkenAutomation.Infrastructure.Configuration;
using WilkenAutomation.Infrastructure.Database;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddWilkenInfrastructure(builder.Configuration);

builder.Services.AddSingleton(builder.Configuration.GetSection(RunDefaults.Section).Get<RunDefaults>() ?? new RunDefaults());
builder.Services.AddScoped<JobGeneratorService>();
builder.Services.AddScoped<RunStatisticsService>();
builder.Services.AddSingleton<WorkerStatusRegistry>();
builder.Services.AddSingleton<IRealtimeNotifier, HubRealtimeNotifier>();

// Allow the configured origins plus any localhost port (the Angular dev server
// falls back to a random port when 4200 is taken).
var configuredOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .SetIsOriginAllowed(origin =>
        configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase) ||
        (Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
         (uri.Host == "localhost" || uri.Host == "127.0.0.1")))
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// Schema creation (MySQL: creates database + tables on first start).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AutomationDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.MapControllers();
app.MapHub<JobMonitoringHub>("/hubs/job-monitoring");

app.Logger.LogInformation("WilkenAutomation API starting. Database provider: {Provider}",
    app.Configuration["Database:Provider"] ?? "MySql");

app.Run();
