using HanakaServer.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HanakaServer.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/tournaments/matches/{matchId:long}/relay")]
public sealed class RelayMatchesController(IOptions<RelayOptions> options) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Get(long matchId)
    {
        if (!options.Value.AdminPreviewEnabled) return NotFound();

        return StatusCode(StatusCodes.Status410Gone, new
        {
            code = "RELAY_MATCH_STATE_RETIRED",
            message = "Trận tiếp sức hiện sử dụng tỷ số chung của hai đội chính."
        });
    }
}
