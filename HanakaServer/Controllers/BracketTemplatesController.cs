using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers;

[Authorize(Roles = "Admin")]
public sealed class BracketTemplatesController : Controller
{
    private readonly PickleballDbContext _db;
    private readonly bool _manualEditorEnabled;

    public BracketTemplatesController(PickleballDbContext db, IConfiguration configuration)
    {
        _db = db;
        _manualEditorEnabled = configuration.GetValue("FeatureFlags:ManualBracketTemplateEditor", true);
    }

    [HttpGet]
    public IActionResult Index() => _manualEditorEnabled ? View() : NotFound();

    [HttpGet]
    public async Task<IActionResult> Editor(long templateId, long? versionId = null, CancellationToken ct = default)
    {
        if (!_manualEditorEnabled)
            return NotFound();

        var template = await _db.BracketTemplates.AsNoTracking()
            .Where(x => x.BracketTemplateId == templateId)
            .Select(x => new
            {
                x.BracketTemplateId,
                x.TemplateCode,
                x.TemplateName,
                VersionId = versionId ?? x.Versions
                    .OrderByDescending(v => v.Status == "DRAFT")
                    .ThenByDescending(v => v.VersionNumber)
                    .Select(v => (long?)v.BracketTemplateVersionId)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        if (template == null || !template.VersionId.HasValue)
            return NotFound();

        var versionBelongsToTemplate = await _db.BracketTemplateVersions.AsNoTracking()
            .AnyAsync(x => x.BracketTemplateVersionId == template.VersionId.Value
                           && x.BracketTemplateId == templateId, ct);
        if (!versionBelongsToTemplate)
            return NotFound();

        ViewBag.TemplateId = template.BracketTemplateId;
        ViewBag.TemplateCode = template.TemplateCode;
        ViewBag.TemplateName = template.TemplateName;
        ViewBag.VersionId = template.VersionId.Value;
        return View();
    }
}
