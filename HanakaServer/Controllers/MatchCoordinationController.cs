using System.Security.Claims;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers;

[ApiController]
[Route("api/coordination")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MatchCoordinationController(MatchCoordinationService service) : ControllerBase
{
    private long? UserId => long.TryParse(User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet("tournaments/{tournamentId:long}/permissions")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Permissions(long tournamentId, CancellationToken ct) => UserId is long id
        ? Ok(new { canCoordinate = await service.CanCoordinateAsync(id, tournamentId, ct) }) : Unauthorized();

    [HttpPut("matches/{matchId:long}")]
    public async Task<IActionResult> Update(long matchId, CoordinateMatchRequest request, CancellationToken ct)
    {
        if (UserId is not long id) return Unauthorized();
        try { return Ok(await service.UpdateAsync(id, matchId, request, ct)); }
        catch (CoordinationException ex) { return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message }); }
    }
}
