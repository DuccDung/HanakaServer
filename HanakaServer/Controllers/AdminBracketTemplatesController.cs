using System.Security.Claims;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Services.Brackets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers;

[ApiController]
[Route("api/admin/bracket-templates")]
[Authorize(Roles = "Admin")]
public sealed class AdminBracketTemplatesController : ControllerBase
{
    private readonly IBracketTemplateService _service;

    public AdminBracketTemplatesController(IBracketTemplateService service)
    {
        _service = service;
    }

    [HttpGet("next-code")]
    public async Task<IActionResult> NextCode(CancellationToken ct)
    {
        return Ok(new { code = await _service.GetNextCodeAsync(ct) });
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? formatType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _service.ListAsync(search, status, formatType, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{templateId:long}")]
    public async Task<IActionResult> Get(long templateId, CancellationToken ct)
    {
        var item = await _service.GetAsync(templateId, ct);
        return item == null ? NotFound(new { message = "Không tìm thấy template." }) : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBracketTemplateRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, CurrentUserId(), ct);
        return ToActionResult(result, created: true);
    }

    [HttpPut("{templateId:long}")]
    public async Task<IActionResult> Update(long templateId, [FromBody] UpdateBracketTemplateRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(templateId, request, CurrentUserId(), ct);
        return ToActionResult(result);
    }

    [HttpPut("{templateId:long}/settings")]
    public async Task<IActionResult> UpdateSettings(
        long templateId,
        [FromBody] UpdateBracketTemplateSettingsRequest request,
        CancellationToken ct)
    {
        var result = await _service.UpdateSettingsAsync(templateId, request, CurrentUserId(), ct);
        return ToActionResult(result);
    }

    [HttpDelete("{templateId:long}")]
    public async Task<IActionResult> Delete(
        long templateId,
        [FromBody] DeleteBracketTemplateRequest request,
        CancellationToken ct)
    {
        var result = await _service.DeleteAsync(templateId, request, ct);
        return ToActionResult(result);
    }

    [HttpPost("clone")]
    public async Task<IActionResult> Clone([FromBody] CloneBracketTemplateRequest request, CancellationToken ct)
    {
        var result = await _service.CloneAsync(
            request.SourceVersionId,
            request.TemplateCode ?? "",
            request.TemplateName ?? "",
            CurrentUserId(),
            ct);
        return ToActionResult(result, created: true);
    }

    [HttpPost("{templateId:long}/archive")]
    public async Task<IActionResult> Archive(long templateId, CancellationToken ct)
    {
        var result = await _service.ArchiveAsync(templateId, CurrentUserId(), ct);
        return ToActionResult(result);
    }

    [HttpPost("{templateId:long}/versions")]
    public async Task<IActionResult> CreateDraftVersion(long templateId, CancellationToken ct)
    {
        var result = await _service.CreateDraftVersionAsync(templateId, CurrentUserId(), ct);
        return ToActionResult(result, created: true);
    }

    [HttpGet("versions/{versionId:long}")]
    public async Task<IActionResult> GetVersion(long versionId, CancellationToken ct)
    {
        var graph = await _service.GetGraphAsync(versionId, ct);
        return graph == null ? NotFound(new { message = "Không tìm thấy template version." }) : Ok(graph);
    }

    [HttpPut("versions/{versionId:long}")]
    public async Task<IActionResult> SaveVersion(
        long versionId,
        [FromBody] SaveBracketTemplateGraphRequest request,
        CancellationToken ct)
    {
        var result = await _service.SaveGraphAsync(versionId, request, ct);
        return ToActionResult(result);
    }

    [HttpPost("versions/{versionId:long}/rounds")]
    public async Task<IActionResult> AddRound(
        long versionId,
        [FromBody] BracketTemplateRoundMutationRequest request,
        CancellationToken ct)
    {
        return ToActionResult(await _service.AddRoundAsync(versionId, request, ct), created: true);
    }

    [HttpPut("versions/{versionId:long}/rounds/{roundKey}")]
    public async Task<IActionResult> UpdateRound(
        long versionId,
        string roundKey,
        [FromBody] BracketTemplateRoundMutationRequest request,
        CancellationToken ct)
    {
        return ToActionResult(await _service.UpdateRoundAsync(versionId, roundKey, request, ct));
    }

    [HttpDelete("versions/{versionId:long}/rounds/{roundKey}")]
    public async Task<IActionResult> DeleteRound(
        long versionId,
        string roundKey,
        [FromBody] BracketTemplateDeleteRequest request,
        CancellationToken ct)
    {
        return ToActionResult(await _service.DeleteRoundAsync(versionId, roundKey, request, ct));
    }

    [HttpPost("versions/{versionId:long}/rounds/{roundKey}/groups")]
    public async Task<IActionResult> AddGroup(
        long versionId,
        string roundKey,
        [FromBody] BracketTemplateGroupMutationRequest request,
        CancellationToken ct)
    {
        return ToActionResult(await _service.AddGroupAsync(versionId, roundKey, request, ct), created: true);
    }

    [HttpPut("versions/{versionId:long}/groups/{groupKey}")]
    public async Task<IActionResult> UpdateGroup(
        long versionId,
        string groupKey,
        [FromBody] BracketTemplateGroupMutationRequest request,
        CancellationToken ct)
    {
        return ToActionResult(await _service.UpdateGroupAsync(versionId, groupKey, request, ct));
    }

    [HttpDelete("versions/{versionId:long}/groups/{groupKey}")]
    public async Task<IActionResult> DeleteGroup(
        long versionId,
        string groupKey,
        [FromBody] BracketTemplateDeleteRequest request,
        CancellationToken ct)
    {
        return ToActionResult(await _service.DeleteGroupAsync(versionId, groupKey, request, ct));
    }

    [HttpPost("versions/{versionId:long}/groups/{groupKey}/matches")]
    public async Task<IActionResult> AddMatch(
        long versionId,
        string groupKey,
        [FromBody] BracketTemplateMatchMutationRequest request,
        CancellationToken ct)
    {
        return ToActionResult(await _service.AddMatchAsync(versionId, groupKey, request, ct), created: true);
    }

    [HttpPut("versions/{versionId:long}/matches/{matchKey}")]
    public async Task<IActionResult> UpdateMatch(
        long versionId,
        string matchKey,
        [FromBody] BracketTemplateMatchMutationRequest request,
        CancellationToken ct)
    {
        return ToActionResult(await _service.UpdateMatchAsync(versionId, matchKey, request, ct));
    }

    [HttpDelete("versions/{versionId:long}/matches/{matchKey}")]
    public async Task<IActionResult> DeleteMatch(
        long versionId,
        string matchKey,
        [FromBody] BracketTemplateDeleteRequest request,
        CancellationToken ct)
    {
        return ToActionResult(await _service.DeleteMatchAsync(versionId, matchKey, request, ct));
    }

    [HttpPut("versions/{versionId:long}/matches/{matchKey}/slots/{slotNumber:int}")]
    public async Task<IActionResult> UpdateSlot(
        long versionId,
        string matchKey,
        byte slotNumber,
        [FromBody] BracketTemplateSlotMutationRequest request,
        CancellationToken ct)
    {
        return ToActionResult(await _service.UpdateSlotAsync(versionId, matchKey, slotNumber, request, ct));
    }

    [HttpGet("versions/{versionId:long}/matches/{matchKey}/source-options")]
    public async Task<IActionResult> GetSourceOptions(
        long versionId,
        string matchKey,
        CancellationToken ct)
    {
        return ToActionResult(await _service.GetSourceOptionsAsync(versionId, matchKey, ct));
    }

    [HttpPost("versions/{versionId:long}/validate")]
    public async Task<IActionResult> Validate(long versionId, CancellationToken ct)
    {
        var result = await _service.ValidateAsync(versionId, ct);
        return ToActionResult(result);
    }

    [HttpPost("versions/{versionId:long}/validate-draft")]
    public async Task<IActionResult> ValidateDraft(
        long versionId,
        [FromBody] SaveBracketTemplateGraphRequest request,
        CancellationToken ct)
    {
        var result = await _service.ValidateDraftAsync(versionId, request, ct);
        return ToActionResult(result);
    }

    [HttpPost("versions/{versionId:long}/generate")]
    public async Task<IActionResult> Generate(
        long versionId,
        [FromBody] GenerateBracketTemplateRequest request,
        CancellationToken ct)
    {
        var result = await _service.GenerateAsync(versionId, request, ct);
        return ToActionResult(result);
    }

    [HttpPost("versions/{versionId:long}/publish")]
    public async Task<IActionResult> Publish(long versionId, CancellationToken ct)
    {
        var result = await _service.PublishAsync(versionId, CurrentUserId(), ct);
        return ToActionResult(result);
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
            "TEMPLATE_NOT_FOUND" or "VERSION_NOT_FOUND" => NotFound(payload),
            "CONCURRENCY_CONFLICT" or "TEMPLATE_CODE_DUPLICATE" or "DRAFT_EXISTS" or "TEMPLATE_IN_USE"
                or "ROUND_KEY_DUPLICATE" or "GROUP_KEY_DUPLICATE" or "MATCH_KEY_DUPLICATE"
                or "ROUND_IN_USE" or "GROUP_IN_USE" or "MATCH_IN_USE" => Conflict(payload),
            _ => BadRequest(payload)
        };
    }
}
