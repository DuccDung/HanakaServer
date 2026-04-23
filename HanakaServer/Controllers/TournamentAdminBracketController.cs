using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [Authorize(Roles = "Admin")]
    public class TournamentAdminBracketController : Controller
    {
        private readonly PickleballDbContext _db;

        public TournamentAdminBracketController(PickleballDbContext db)
        {
            _db = db;
        }

        // /TournamentAdminBracket/Index?tournamentId=123
        public async Task<IActionResult> Index(long tournamentId)
        {
            var tournament = await _db.Tournaments.AsNoTracking()
                .Where(x => x.TournamentId == tournamentId)
                .Select(x => new
                {
                    x.TournamentId,
                    x.Title,
                    x.Status,
                    x.GameType,
                    x.ExpectedTeams,
                    x.StartTime,
                    x.RegisterDeadline
                })
                .FirstOrDefaultAsync();

            if (tournament == null)
            {
                return NotFound();
            }

            ViewBag.TournamentId = tournament.TournamentId;
            ViewBag.TournamentTitle = tournament.Title;
            ViewBag.TournamentStatus = tournament.Status;
            ViewBag.TournamentGameType = tournament.GameType;
            ViewBag.ExpectedTeams = tournament.ExpectedTeams;
            ViewBag.StartTime = tournament.StartTime;
            ViewBag.RegisterDeadline = tournament.RegisterDeadline;

            return View();
        }
    }
}
