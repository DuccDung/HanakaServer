using System.Security.Claims;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Services;
using HanakaServer.Services.Brackets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers;

[ApiController]
[Route("api/admin/tournaments/{tournamentId:long}/bracket")]
[Authorize(Roles = "Admin")]
public sealed class AdminTournamentBracketApplicationsController : ControllerBase
{
    private readonly ITournamentBracketApplicationService _service;
    private readonly ITournamentBracketPropagationService _propagationService;

    public AdminTournamentBracketApplicationsController(
        ITournamentBracketApplicationService service,
        ITournamentBracketPropagationService propagationService)
    {
        _service = service;
        _propagationService = propagationService;
    }

    [HttpGet("templates")]
    public async Task<IActionResult> Templates(long tournamentId, CancellationToken ct)
    {
        var items = await _service.GetApplicableTemplatesAsync(tournamentId, ct);
        return Ok(new { items });
    }

    [HttpGet("eligible-registrations")]
    public async Task<IActionResult> EligibleRegistrations(long tournamentId, CancellationToken ct)
    {
        var result = await _service.GetEligibleRegistrationsAsync(tournamentId, ct);
        return ToActionResult(result);
    }

    [HttpPost("registration-lock")]
    public async Task<IActionResult> SetRegistrationLock(
        long tournamentId,
        [FromBody] SetTournamentRegistrationLockRequest request,
        CancellationToken ct)
    {
        var result = await _service.SetRegistrationLockAsync(tournamentId, request.Locked, CurrentUserId(), ct);
        return ToActionResult(result);
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview(
        long tournamentId,
        [FromBody] TournamentBracketPreviewRequest request,
        CancellationToken ct)
    {
        var result = await _service.PreviewAsync(tournamentId, request, ct);
        return ToActionResult(result);
    }

    [HttpPost("apply")]
    public async Task<IActionResult> Apply(
        long tournamentId,
        [FromBody] ApplyTournamentBracketRequest request,
        CancellationToken ct)
    {
        var result = await _service.ApplyAsync(tournamentId, request, CurrentUserId(), ct);
        return ToActionResult(result, created: true);
    }

    [HttpGet("application")]
    public async Task<IActionResult> ActiveApplication(long tournamentId, CancellationToken ct)
    {
        var item = await _service.GetActiveApplicationAsync(tournamentId, ct);
        return Ok(new { item });
    }

    [HttpGet("application-history")]
    public async Task<IActionResult> ApplicationHistory(long tournamentId, CancellationToken ct)
    {
        var items = await _service.GetApplicationHistoryAsync(tournamentId, ct);
        return Ok(new { items });
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset(
        long tournamentId,
        [FromBody] ResetTournamentBracketRequest request,
        CancellationToken ct)
    {
        var result = await _service.ResetAsync(tournamentId, request, CurrentUserId(), ct);
        return ToActionResult(result);
    }

    [HttpPost("reconcile")]
    public async Task<IActionResult> Reconcile(long tournamentId, CancellationToken ct)
    {
        var result = await _propagationService.ReconcileTournamentAsync(tournamentId, ct);
        return Ok(new
        {
            data = result,
            message = result.UnresolvedSlotCount == 0
                ? $"Đã kiểm tra đường đi tiếp; phục hồi {result.ResolvedSlotCount} slot."
                : $"Đã kiểm tra nhưng còn {result.UnresolvedSlotCount} slot chưa đủ kết quả nguồn."
        });
    }

    private long? CurrentUserId()
    {
        var raw = User.FindFirstValue("uid")
                  ?? User.FindFirstValue("UserId")
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(raw, out var userId) ? userId : null;
    }

    private IActionResult ToActionResult<T>(BracketOperationResult<T> result, bool created = false)
    {
        if (result.Success)
            return created ? StatusCode(StatusCodes.Status201Created, new { data = result.Data, result.Message }) : Ok(new { data = result.Data, result.Message });

        var payload = new { code = result.ErrorCode, message = result.Message };
        return result.ErrorCode switch
        {
            "TOURNAMENT_NOT_FOUND" or "VERSION_NOT_FOUND" or "APPLICATION_NOT_FOUND" => NotFound(payload),
            "ACTIVE_APPLICATION_EXISTS" or "RUNTIME_STRUCTURE_EXISTS" or "TOURNAMENT_ALREADY_STARTED" or "PREVIEW_CHANGED" => Conflict(payload),
            _ => BadRequest(payload)
        };
    }
}
