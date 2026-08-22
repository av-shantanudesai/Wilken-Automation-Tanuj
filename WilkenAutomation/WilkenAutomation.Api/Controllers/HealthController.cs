using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WilkenAutomation.Api.Services;
using WilkenAutomation.Infrastructure.Database;

namespace WilkenAutomation.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly AutomationDbContext _db;
    private readonly WorkerStatusRegistry _worker;

    public HealthController(AutomationDbContext db, WorkerStatusRegistry worker)
    {
        _db = db;
        _worker = worker;
    }

    [HttpGet]
    public IActionResult Live() => Ok(new { status = "ok", at = DateTime.UtcNow });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var dbOk = await _db.Database.CanConnectAsync(ct);
        var snap = _worker.Snapshot();
        var workerAlive = snap.WorkerState is not "STOPPED";
        var payload = new
        {
            status = dbOk ? "ok" : "degraded",
            database = dbOk,
            workerState = snap.WorkerState,
            workerAlive,
            wilkenSessionStatus = snap.WilkenSessionStatus,
            heartbeatAt = snap.HeartbeatAt,
            at = DateTime.UtcNow
        };
        return dbOk ? Ok(payload) : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }
}
