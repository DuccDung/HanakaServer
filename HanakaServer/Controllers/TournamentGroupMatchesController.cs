using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [Authorize(Roles = "Admin")]
    public class TournamentGroupMatchesController : Controller
    {
        private readonly PickleballDbContext _db;

        public TournamentGroupMatchesController(PickleballDbContext db)
        {
            _db = db;
        }

        // /TournamentGroupMatches/Index?groupId=123
        public async Task<IActionResult> Index(long groupId)
        {
            var g = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => x.TournamentRoundGroupId == groupId)
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.TournamentRoundMapId,
                    x.GroupName
                })
                .FirstOrDefaultAsync();

            if (g == null) return NotFound();

            var rm = await _db.TournamentRoundMaps.AsNoTracking()
                .Where(x => x.TournamentRoundMapId == g.TournamentRoundMapId)
                .Select(x => new
                {
                    x.TournamentRoundMapId,
                    x.TournamentId,
                    x.RoundKey,
                    x.RoundLabel
                })
                .FirstOrDefaultAsync();

            if (rm == null) return NotFound();

            var t = await _db.Tournaments.AsNoTracking()
                .Where(x => x.TournamentId == rm.TournamentId)
                .Select(x => new
                {
                    x.TournamentId,
                    x.Title,
                    x.Status,
                    x.GameType
                })
                .FirstOrDefaultAsync();

            if (t == null) return NotFound();

            ViewBag.GroupId = g.TournamentRoundGroupId;
            ViewBag.GroupName = g.GroupName;
            ViewBag.RoundMapId = rm.TournamentRoundMapId;
            ViewBag.RoundKey = rm.RoundKey;
            ViewBag.RoundLabel = rm.RoundLabel;

            ViewBag.TournamentId = rm.TournamentId;
            ViewBag.TournamentTitle = t.Title;
            ViewBag.TournamentStatus = t.Status;
            ViewBag.TournamentGameType = t.GameType;

            return View();
        }
    }
}