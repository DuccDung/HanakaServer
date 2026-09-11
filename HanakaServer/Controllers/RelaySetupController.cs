using HanakaServer.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HanakaServer.Controllers;

[Authorize(Roles = "Admin")]
public sealed class RelaySetupController(IOptions<RelayOptions> options) : Controller
{
    [HttpGet]
    public IActionResult Index(long tournamentId)
    {
        if (!options.Value.AdminPreviewEnabled || tournamentId <= 0) return NotFound();
        return View(tournamentId);
    }
}
