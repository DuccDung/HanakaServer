using HanakaServer.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HanakaServer.Controllers;

[ApiController]
[Authorize(Roles = "REFEREE,Admin")]
[Route("api/referee/matches/{matchId:long}/relay")]
public sealed class RefereeRelayMatchesController(IOptions<RelayOptions> options) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Get(long matchId) => Retired(matchId);

    [HttpPost("commands")]
    public IActionResult Execute(long matchId) => Retired(matchId);

    private IActionResult Retired(long matchId)
    {
        if (!options.Value.AdminPreviewEnabled) return NotFound();

        return StatusCode(StatusCodes.Status410Gone, new
        {
            code = "RELAY_SCORING_RETIRED",
            message = "Trận tiếp sức hiện được chấm trực tiếp theo tổng điểm của hai đội.",
            redirectUrl = $"/RefereePortal/Matches/{matchId}"
        });
    }
}
