using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [Authorize(Roles = "Admin")]
    public class TournamentRoundGroupsController : Controller
    {
        private readonly PickleballDbContext _db;
        public TournamentRoundGroupsController(PickleballDbContext db) { _db = db; }

        // /TournamentRoundGroups/Index?roundMapId=123
        public async Task<IActionResult> Index(long roundMapId)
        {
            var rm = await _db.TournamentRoundMaps.AsNoTracking()
                .Where(x => x.TournamentRoundMapId == roundMapId)
                .Select(x => new { x.TournamentRoundMapId, x.TournamentId, x.RoundKey, x.RoundLabel })
                .FirstOrDefaultAsync();

            if (rm == null) return NotFound();

            var t = await _db.Tournaments.AsNoTracking()
                .Where(x => x.TournamentId == rm.TournamentId)
                .Select(x => new { x.TournamentId, x.Title, x.Status, x.GameType })
                .FirstOrDefaultAsync();

            ViewBag.RoundMapId = rm.TournamentRoundMapId;
            ViewBag.TournamentId = rm.TournamentId;
            ViewBag.RoundKey = rm.RoundKey;
            ViewBag.RoundLabel = rm.RoundLabel;

            ViewBag.TournamentTitle = t?.Title;
            ViewBag.TournamentStatus = t?.Status;
            ViewBag.TournamentGameType = t?.GameType;

            return View();
        }
    }
}