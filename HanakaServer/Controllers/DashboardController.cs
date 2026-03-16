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
            var model = new DashboardViewModel
            {
                TotalUsers = await _db.Users.CountAsync(),
                InactiveUsers = await _db.Users.CountAsync(x => !x.IsActive),
                VerifiedUsers = await _db.Users.CountAsync(x => x.Verified),
                UnverifiedUsers = await _db.Users.CountAsync(x => !x.Verified),

                TotalTournaments = await _db.Tournaments.CountAsync(),
                ActiveTournaments = await _db.Tournaments.CountAsync(x => x.Status == "OPEN" || x.Status == "ACTIVE"),

                TotalBanners = await _db.Banners.CountAsync(),
                ActiveBanners = await _db.Banners.CountAsync(x => x.IsActive),

                TotalClubs = await _db.Clubs.CountAsync(),
                ActiveClubs = await _db.Clubs.CountAsync(x => x.IsActive),

                TotalCoaches = await _db.Coaches.CountAsync(),
                VerifiedCoaches = await _db.Coaches.CountAsync(x => x.Verified),

                TotalReferees = await _db.Referees.CountAsync(),
                VerifiedReferees = await _db.Referees.CountAsync(x => x.Verified),

                TotalCourts = await _db.Courts.CountAsync()
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
                .OrderByDescending(x => x.CreatedAt)
                .Take(5)
                .Select(x => new RecentTournamentItem
                {
                    TournamentId = x.TournamentId,
                    Title = x.Title,
                    Status = x.Status,
                    RegisteredCount = x.RegisteredCount,
                    MatchesCount = x.MatchesCount,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();

            return View(model);
        }
    }
}