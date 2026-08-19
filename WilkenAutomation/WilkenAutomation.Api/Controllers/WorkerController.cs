using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WilkenAutomation.Api.Auth;
using WilkenAutomation.Api.Services;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/worker")]
public class WorkerController : ControllerBase
{
    private readonly WorkerStatusRegistry _registry;
    private readonly IRunRepository _runs;

    public WorkerController(WorkerStatusRegistry registry, IRunRepository runs)
    {
        _registry = registry;
        _runs = runs;
    }

    [HttpGet("status")]
    public async Task<ActionResult<WorkerStatusDto>> Status(CancellationToken ct)
    {
        var snap = _registry.Snapshot();
        if (string.IsNullOrWhiteSpace(snap.CurrentRunId))
            return WorkerStatusScope.ForStranger(snap);

        var run = await _runs.GetByRunIdAsync(snap.CurrentRunId, ct);
        if (run is null || run.UserId != User.GetRequiredUserId())
            return WorkerStatusScope.ForStranger(snap);

        return WorkerStatusScope.ForOwner(snap);
    }
}
