using HanakaServer.Data;
using HanakaServer.Dtos.Relay;
using HanakaServer.Options;
using HanakaServer.Services.Relay;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HanakaServer.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/tournaments/{tournamentId:long}/relay")]
public sealed class AdminRelayController(PickleballDbContext db, RelayAdminService admin,
    RelayLineupService lineups, IOptions<RelayOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long tournamentId, CancellationToken ct)
    {
        if (!options.Value.AdminPreviewEnabled) return Unavailable();
        var tournament = await db.Tournaments.AsNoTracking().Where(x => x.TournamentId == tournamentId && !x.Remove)
            .Select(x => new { x.TournamentId, x.Title, x.ExpectedTeams }).SingleOrDefaultAsync(ct);
        if (tournament == null) return NotFound(new { message = "Không tìm thấy giải đấu." });
        var settings = await db.RelayTournamentSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TournamentId == tournamentId, ct);
        var teams = await db.RelayTeams.AsNoTracking().Include(x => x.Members).Where(x => x.TournamentId == tournamentId)
            .OrderBy(x => x.RegistrationId).ToListAsync(ct);
        var registrations = await db.TournamentRegistrations.AsNoTracking()
            .Where(x => x.TournamentId == tournamentId && !x.IsVirtualTeam)
            .OrderBy(x => x.RegIndex).Select(x => new { x.RegistrationId, x.RegCode, x.Player1Name, x.Player2Name }).ToListAsync(ct);
        return Ok(new { tournament, settings = settings == null ? null : RelayAdminService.MapSettings(settings),
            teams = teams.Select(x => RelayAdminService.MapTeam(x, settings?.TeamSize)), registrations });
    }

    [HttpPut("settings")]
    public Task<IActionResult> SaveSettings(long tournamentId, SaveRelaySettingsRequest request, CancellationToken ct) =>
        RunAsync(async () => Ok(await admin.SaveSettingsAsync(tournamentId, request, ct)));

    [HttpPost("activate")]
    public Task<IActionResult> Activate(long tournamentId, SetRelayEnabledRequest request, CancellationToken ct) =>
        RunAsync(async () => Ok(await admin.ActivateAsync(tournamentId, request, ct)));

    [HttpPut("teams/{registrationId:long}")]
    public Task<IActionResult> SaveTeam(long tournamentId, long registrationId, SaveRelayTeamRequest request, CancellationToken ct) =>
        RunAsync(async () =>
        {
            var team = await lineups.SaveDraftAsync(registrationId, tournamentId,
                request.TeamName, request.CaptainUserId, request.Members, request.ExpectedVersion, ct);
            var teamSize = await db.RelayTournamentSettings.Where(x => x.TournamentId == tournamentId)
                .Select(x => x.TeamSize).SingleAsync(ct);
            return Ok(RelayAdminService.MapTeam(team, teamSize));
        });

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        if (!options.Value.AdminPreviewEnabled) return Unavailable();
        try { return await action(); }
        catch (RelayRuleException ex)
        {
            var status = ex.Code == "VERSION_CONFLICT" ? 409 : ex.Code.EndsWith("NOT_FOUND") ? 404 : 400;
            return StatusCode(status, new { code = ex.Code, message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = "VERSION_CONFLICT", message = "Dữ liệu đã thay đổi; vui lòng tải lại." });
        }
    }

    private IActionResult Unavailable() => NotFound(new { message = "Chức năng chuẩn bị đội tiếp sức chưa được bật." });
}
