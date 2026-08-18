using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WilkenAutomation.Api.Services;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/worker")]
public class WorkerController : ControllerBase
{
    private readonly WorkerStatusRegistry _registry;

    public WorkerController(WorkerStatusRegistry registry)
    {
        _registry = registry;
    }

    [HttpGet("status")]
    public ActionResult<WorkerStatusDto> Status() => _registry.Snapshot();
}
