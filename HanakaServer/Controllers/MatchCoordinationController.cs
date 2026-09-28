using System.Security.Claims;
using HanakaServer.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers;

[ApiController]
[Route("api/coordination")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MatchCoordinationController(MatchCoordinationService service, IAntiforgery antiforgery) : ControllerBase
{
    private long? UserId => long.TryParse(User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // Match Program.cs: a nonempty bearer token takes precedence over the web cookie.
    private bool UsesWebCookie => Request.Cookies.ContainsKey(WebAuthCookieService.AccessTokenCookieName)
        && !(Request.Headers.Authorization.ToString() is var header
            && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(header["Bearer ".Length..]));

    [HttpGet("tournaments/{tournamentId:long}/permissions")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Permissions(long tournamentId, CancellationToken ct)
    {
        if (UserId is not long id) return Unauthorized();
        var canCoordinate = await service.CanCoordinateAsync(id, tournamentId, ct);
        var requestToken = canCoordinate && UsesWebCookie ? antiforgery.GetAndStoreTokens(HttpContext).RequestToken : null;
        return Ok(new { canCoordinate, userId = id, requestToken });
    }

    [HttpPut("matches/{matchId:long}")]
    public async Task<IActionResult> Update(long matchId, CoordinateMatchRequest request, CancellationToken ct)
    {
        if (UserId is not long id) return Unauthorized();
        if (UsesWebCookie)
        {
            try { await antiforgery.ValidateRequestAsync(HttpContext); }
            catch (AntiforgeryValidationException)
            {
                return BadRequest(new { code = "INVALID_WEB_TOKEN", message = "Phiên xác nhận đã hết hạn. Vui lòng tải lại dữ liệu trước khi lưu." });
            }
        }
        try { return Ok(await service.UpdateAsync(id, matchId, request, ct)); }
        catch (CoordinationException ex) { return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message }); }
    }
}
