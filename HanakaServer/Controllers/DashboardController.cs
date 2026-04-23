using HanakaServer.Data;
using HanakaServer.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [Authorize(Roles = "Admin")]
    public class DashboardController : Controller
    {
        private readonly PickleballDbContext _db;

        public DashboardController(PickleballDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var now = DateTime.Now;

            var model = new DashboardViewModel
            {
                TotalUsers = await _db.Users.CountAsync(),
                InactiveUsers = await _db.Users.CountAsync(x => !x.IsActive),
                VerifiedUsers = await _db.Users.CountAsync(x => x.Verified),
                UnverifiedUsers = await _db.Users.CountAsync(x => !x.Verified),

                TotalTournaments = await _db.Tournaments.CountAsync(x => !x.Remove),
                ActiveTournaments = await _db.Tournaments.CountAsync(x => !x.Remove && (x.Status == "OPEN" || x.Status == "ACTIVE")),
                CompletedTournaments = await _db.Tournaments.CountAsync(x => !x.Remove && x.Status == "COMPLETED"),
                RemovedTournaments = await _db.Tournaments.CountAsync(x => x.Remove),

                TotalRegistrations = await _db.TournamentRegistrations.CountAsync(x => !x.Tournament.Remove),
                SuccessfulRegistrations = await _db.TournamentRegistrations.CountAsync(x => !x.Tournament.Remove && x.Success),
                PaidRegistrations = await _db.TournamentRegistrations.CountAsync(x => !x.Tournament.Remove && x.Paid),
                WaitingPairRegistrations = await _db.TournamentRegistrations.CountAsync(x => !x.Tournament.Remove && x.WaitingPair),

                TotalMatches = await _db.TournamentGroupMatches.CountAsync(x => !x.Tournament.Remove),
                CompletedMatches = await _db.TournamentGroupMatches.CountAsync(x => !x.Tournament.Remove && x.IsCompleted),
                UpcomingMatches = await _db.TournamentGroupMatches.CountAsync(x => !x.Tournament.Remove && !x.IsCompleted && x.StartAt != null && x.StartAt >= now),

                TotalRoundMaps = await _db.TournamentRoundMaps.CountAsync(x => !x.Tournament.Remove),
                TotalRoundGroups = await _db.TournamentRoundGroups.CountAsync(x => !x.TournamentRoundMap.Tournament.Remove),

                TotalBanners = await _db.Banners.CountAsync(),
                ActiveBanners = await _db.Banners.CountAsync(x => x.IsActive),

                TotalClubs = await _db.Clubs.CountAsync(),
                ActiveClubs = await _db.Clubs.CountAsync(x => x.IsActive),

                TotalCoaches = await _db.Coaches.CountAsync(),
                VerifiedCoaches = await _db.Coaches.CountAsync(x => x.Verified),

                TotalReferees = await _db.Referees.CountAsync(),
                VerifiedReferees = await _db.Referees.CountAsync(x => x.Verified),

                TotalCourts = await _db.Courts.CountAsync(),
                TotalLinks = await _db.Links.CountAsync()
            };

            model.RoleStats = await _db.UserRoles
                .Include(x => x.Role)
                .GroupBy(x => new { x.RoleId, x.Role.RoleName })
                .Select(g => new RoleStatItem
                {
                    RoleId = g.Key.RoleId,
                    RoleName = g.Key.RoleName,
                    UserCount = g.Select(x => x.UserId).Distinct().Count()
                })
                .OrderBy(x => x.RoleId)
                .ToListAsync();

            model.RecentTournaments = await _db.Tournaments
                .AsNoTracking()
                .Where(x => !x.Remove)
                .OrderByDescending(x => x.CreatedAt)
                .Take(5)
                .Select(x => new RecentTournamentItem
                {
                    TournamentId = x.TournamentId,
                    Title = x.Title,
                    Status = x.Status,
                    RegisteredCount = _db.TournamentRegistrations.Count(reg => reg.TournamentId == x.TournamentId),
                    MatchesCount = _db.TournamentGroupMatches.Count(match => match.TournamentId == x.TournamentId),
                    CompletedMatchesCount = _db.TournamentGroupMatches.Count(match => match.TournamentId == x.TournamentId && match.IsCompleted),
                    RoundCount = _db.TournamentRoundMaps.Count(round => round.TournamentId == x.TournamentId),
                    GroupCount = _db.TournamentRoundGroups.Count(group => group.TournamentRoundMap.TournamentId == x.TournamentId),
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();

            return View(model);
        }
    }
}
