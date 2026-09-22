using System.Data;
using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers;

[ApiController]
[Authorize(Roles = RoleCodes.Admin)]
[Route("api/admin/tournaments/{tournamentId:long}/coordinators")]
public sealed class AdminTournamentCoordinatorsController(PickleballDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(long tournamentId, CancellationToken ct) => Ok(await
        (from a in db.TournamentCoordinators.AsNoTracking()
         join u in db.Users on a.UserId equals u.UserId
         where a.TournamentId == tournamentId
         orderby u.FullName
         select new { a.UserId, u.FullName, u.Phone, u.IsActive, a.CreatedAt }).ToListAsync(ct));

    [HttpPut("{userId:long}")]
    public async Task<IActionResult> Assign(long tournamentId, long userId, CancellationToken ct)
    {
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        if (!await db.Tournaments.AnyAsync(t => t.TournamentId == tournamentId && !t.Remove, ct)
            || !await db.Users.AnyAsync(u => u.UserId == userId && u.IsActive, ct))
            return BadRequest(new { message = "Giải hoặc tài khoản không hợp lệ." });
        var role = await db.Roles.SingleOrDefaultAsync(x => x.RoleCode == RoleCodes.Coordinator, ct);
        if (role == null)
        {
            role = new Role { RoleCode = RoleCodes.Coordinator, RoleName = "Điều phối giải đấu" };
            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);
        }
        if (!await db.UserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == role.RoleId, ct))
            db.UserRoles.Add(new() { UserId = userId, RoleId = role.RoleId, CreatedAt = DateTime.UtcNow });
        if (!await db.TournamentCoordinators.AnyAsync(x => x.UserId == userId && x.TournamentId == tournamentId, ct))
            db.TournamentCoordinators.Add(new() { UserId = userId, TournamentId = tournamentId, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpDelete("{userId:long}")]
    public async Task<IActionResult> Revoke(long tournamentId, long userId, CancellationToken ct)
    {
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var assignment = await db.TournamentCoordinators.SingleOrDefaultAsync(x => x.UserId == userId && x.TournamentId == tournamentId, ct);
        if (assignment != null) db.TournamentCoordinators.Remove(assignment);
        if (!await db.TournamentCoordinators.AnyAsync(x => x.UserId == userId && x.TournamentId != tournamentId, ct))
        {
            var roles = await db.UserRoles.Where(x => x.UserId == userId && x.Role.RoleCode == RoleCodes.Coordinator).ToListAsync(ct);
            db.UserRoles.RemoveRange(roles);
        }
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return Ok(new { ok = true });
    }
}
