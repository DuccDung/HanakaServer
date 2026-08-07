using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers;

[Authorize(Roles = "Admin")]
public sealed class TournamentBracketSetupController : Controller
{
    private readonly PickleballDbContext _db;

    public TournamentBracketSetupController(PickleballDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(long tournamentId, CancellationToken ct = default)
    {
        var tournament = await _db.Tournaments.AsNoTracking()
            .Where(x => x.TournamentId == tournamentId)
            .Select(x => new
            {
                x.TournamentId,
                x.Title,
                x.Status,
                x.ExpectedTeams,
                x.StartTime,
                x.LocationText,
                x.RegistrationLockedAt
            })
            .FirstOrDefaultAsync(ct);

        if (tournament == null)
            return NotFound();

        ViewBag.TournamentId = tournament.TournamentId;
        ViewBag.TournamentTitle = tournament.Title;
        ViewBag.TournamentStatus = tournament.Status;
        ViewBag.ExpectedTeams = tournament.ExpectedTeams;
        ViewBag.TournamentStartTime = tournament.StartTime;
        ViewBag.TournamentLocation = tournament.LocationText;
        ViewBag.RegistrationLocked = tournament.RegistrationLockedAt.HasValue;
        return View();
    }
}
