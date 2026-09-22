using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers;

[ApiController]
[Route("api/coordinator-portal")]
[Authorize(AuthenticationSchemes = CoordinatorPortalAuthentication.Scheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CoordinatorPortalApiController(PickleballDbContext db, MatchCoordinationService service) : ControllerBase
{
    private long UserId => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public sealed class LoginRequest
    {
        [Required, StringLength(256)] public string Account { get; set; } = "";
        [Required, StringLength(256)] public string Password { get; set; } = "";
        public bool RememberMe { get; set; }
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (!service.Enabled) return StatusCode(403, new { message = "Chức năng điều phối đang tạm tắt." });
        var account = request.Account.Trim();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.IsActive
            && (u.Email == account || u.Phone == account), ct);
        var passwordOk = false;
        if (!string.IsNullOrWhiteSpace(user?.PasswordHash))
        {
            try { passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash); }
            catch (BCrypt.Net.SaltParseException) { /* Accounts created through OTP may not have a password hash. */ }
        }
        if (!passwordOk || user == null)
            return Unauthorized(new { message = "Tài khoản hoặc mật khẩu không đúng, hoặc tài khoản đã bị khóa." });
        var assigned = await db.TournamentCoordinators.AsNoTracking().AnyAsync(a => a.UserId == user.UserId
            && db.Tournaments.Any(t => t.TournamentId == a.TournamentId && !t.Remove)
            && db.UserRoles.Any(r => r.UserId == user.UserId && r.Role.RoleCode == RoleCodes.Coordinator), ct);
        if (!assigned) return StatusCode(403, new { message = "Tài khoản chưa được phân công điều phối giải. Vui lòng liên hệ ban tổ chức." });

        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, RoleCodes.Coordinator)
        ], CoordinatorPortalAuthentication.Scheme));
        await HttpContext.SignInAsync(CoordinatorPortalAuthentication.Scheme, principal,
            new AuthenticationProperties { IsPersistent = request.RememberMe });
        return Ok(new { redirectUrl = "/CoordinatorPortal/Matches" });
    }

    [HttpGet("session")]
    public async Task<IActionResult> Session(CancellationToken ct)
    {
        if (!service.Enabled) return StatusCode(403, new { message = "Chức năng điều phối đang tạm tắt." });
        var userId = UserId;
        var tournaments = await (from a in db.TournamentCoordinators.AsNoTracking()
                                 join t in db.Tournaments.AsNoTracking() on a.TournamentId equals t.TournamentId
                                 where a.UserId == userId && !t.Remove
                                 orderby t.TournamentId descending
                                 select new { t.TournamentId, t.Title }).ToListAsync(ct);
        return Ok(new { userId, fullName = User.Identity?.Name, tournaments });
    }

    [HttpPut("matches/{matchId:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(long matchId, CoordinateMatchRequest request, CancellationToken ct)
    {
        try { return Ok(await service.UpdateAsync(UserId, matchId, request, ct)); }
        catch (CoordinationException ex) { return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message }); }
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CoordinatorPortalAuthentication.Scheme);
        return Ok(new { redirectUrl = "/CoordinatorPortal/Login" });
    }
}
