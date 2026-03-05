using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [Authorize(Roles = "Admin")]
    public class TournamentRoundsController : Controller
    {
        private readonly PickleballDbContext _db;
        public TournamentRoundsController(PickleballDbContext db) { _db = db; }

        // /TournamentRounds/Index?tournamentId=123
        public async Task<IActionResult> Index(long tournamentId)
        {
            var t = await _db.Tournaments.AsNoTracking()
                .Where(x => x.TournamentId == tournamentId)
                .Select(x => new { x.TournamentId, x.Title, x.Status, x.GameType })
                .FirstOrDefaultAsync();

            if (t == null) return NotFound();

            ViewBag.TournamentId = t.TournamentId;
            ViewBag.TournamentTitle = t.Title;
            ViewBag.TournamentStatus = t.Status;
            ViewBag.TournamentGameType = t.GameType;

            return View();
        }
    }
}