using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/export-definitions")]
public class ExportDefinitionsController : ControllerBase
{
    private readonly ExportDefinitionCatalog _catalog;

    public ExportDefinitionsController(ExportDefinitionCatalog catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    public ActionResult<List<ExportDefinitionDto>> List() =>
        Ok(_catalog.All.Select(d => new ExportDefinitionDto
        {
            Name = d.Name,
            Type = d.Type,
            Module = d.Module,
            DisplayName = string.IsNullOrWhiteSpace(d.DisplayName) ? d.Name : d.DisplayName,
            Requires = d.Requires,
            Format = d.Export.Format
        }).ToList());
}
