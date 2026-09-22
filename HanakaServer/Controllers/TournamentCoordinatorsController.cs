using HanakaServer.Data;
using HanakaServer.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers;

[Authorize(Roles = RoleCodes.Admin)]
public sealed class TournamentCoordinatorsController(PickleballDbContext db) : Controller
{
    public async Task<IActionResult> Index(long tournamentId, CancellationToken ct)
    {
        var tournament = await db.Tournaments.AsNoTracking().Where(x => x.TournamentId == tournamentId && !x.Remove)
            .Select(x => new { x.TournamentId, x.Title }).SingleOrDefaultAsync(ct);
        if (tournament == null) return NotFound();
        ViewBag.TournamentId = tournamentId;
        ViewBag.TournamentTitle = tournament.Title;
        return View();
    }
}
