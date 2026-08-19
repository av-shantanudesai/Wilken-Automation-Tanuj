// DEPRECATED: this project is superseded by WilkenAutomation/. Do not add features here.
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WilkenExport.Api.Adapters;
using WilkenExport.Api.Configuration;
using WilkenExport.Api.Data;
using WilkenExport.Api.Engine;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<ExportOptions>(builder.Configuration.GetSection("Export"));

var dbProvider = builder.Configuration["Database:Provider"] ?? "InMemory";
builder.Services.AddDbContext<ExportDbContext>(options =>
{
    if (string.Equals(dbProvider, "MySql", StringComparison.OrdinalIgnoreCase))
    {
        var cs = builder.Configuration["Database:ConnectionString"]
                 ?? throw new InvalidOperationException("Database:ConnectionString is required for the MySql provider.");
        options.UseMySql(cs, new MySqlServerVersion(new Version(8, 0, 36)));
    }
    else
    {
        options.UseInMemoryDatabase("wilken-export");
    }
});

builder.Services.AddSingleton<WorkerStatusService>();
builder.Services.AddSingleton<ChecksumService>();
builder.Services.AddSingleton<FileValidator>();
builder.Services.AddSingleton<IWilkenAdapter, SimulatedWilkenAdapter>();
builder.Services.AddScoped<JobGenerator>();
builder.Services.AddScoped<RunVerificationService>();
builder.Services.AddHostedService<ExportWorker>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.MapControllers();

app.Run();
