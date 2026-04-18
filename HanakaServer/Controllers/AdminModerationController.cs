using HanakaServer.Data;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/admin/moderation")]
    [Authorize(Roles = "Admin")]
    public class AdminModerationController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly RealtimeHub _realtimeHub;

        public AdminModerationController(
            PickleballDbContext db,
            RealtimeHub realtimeHub)
        {
            _db = db;
            _realtimeHub = realtimeHub;
        }

        [HttpGet("reports")]
        public async Task<IActionResult> GetReports(
            [FromQuery] string? status = "PENDING",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 100) pageSize = 100;

            var normalizedStatus = (status ?? "PENDING").Trim().ToUpperInvariant();
            var q = _db.ModerationReports.AsNoTracking();

            if (normalizedStatus != "ALL")
            {
                q = q.Where(x => x.Status == normalizedStatus);
            }

            var total = await q.CountAsync();
            var now = DateTime.UtcNow;

            var items = await q
                .OrderBy(x => x.Status == "PENDING" ? 0 : 1)
                .ThenBy(x => x.SlaDueAt)
                .ThenByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    reportId = x.ReportId,
                    kind = x.ReportType,
                    reason = x.ReasonCode,
                    reasonLabel = x.ReasonLabel,
                    status = x.Status,
                    source = x.Source,
                    notes = x.Notes,
                    clubId = x.ClubId,
                    clubName = x.Club != null ? x.Club.ClubName : null,
                    messageId = x.MessageId,
                    messageContent = x.MessageContentSnapshot,
                    reporterUserId = x.ReporterUserId,
                    reporterName = x.ReporterNameSnapshot ?? x.ReporterUser.FullName,
                    targetUserId = x.TargetUserId,
                    targetUserName = x.TargetNameSnapshot ?? (x.TargetUser != null ? x.TargetUser.FullName : null),
                    developerNotified = x.DeveloperNotified,
                    developerNotifiedAt = x.DeveloperNotifiedAt,
                    slaDueAt = x.SlaDueAt,
                    isOverdue = x.Status != "RESOLVED" && x.SlaDueAt < now,
                    reviewedByUserId = x.ReviewedByUserId,
                    reviewedByName = x.ReviewedByName,
                    reviewedAt = x.ReviewedAt,
                    resolutionAction = x.ResolutionAction,
                    resolutionNote = x.ResolutionNote,
                    resolvedAt = x.ResolvedAt,
                    createdAt = x.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                total,
                page,
                pageSize,
                status = normalizedStatus,
                items
            });
        }

        [HttpPost("messages/{messageId:long}/hide")]
        public async Task<IActionResult> HideClubMessage(long messageId)
        {
            var message = await _db.ClubMessages
                .FirstOrDefaultAsync(x => x.MessageId == messageId);

            if (message == null)
            {
                return NotFound(new { message = "Message not found." });
            }

            if (!message.IsDeleted)
            {
                message.IsDeleted = true;
                message.Content = null;
                message.MediaUrl = null;
                await _db.SaveChangesAsync();
                await _realtimeHub.SendClubMessageDeletedAsync(message.ClubId, message.MessageId);
            }

            return Ok(new
            {
                messageId = message.MessageId,
                clubId = message.ClubId,
                isDeleted = true
            });
        }

        [HttpPost("reports/{reportId:long}/resolve")]
        public async Task<IActionResult> ResolveReport(long reportId, [FromBody] ModerationResolveReportDto? dto)
        {
            dto ??= new ModerationResolveReportDto();

            var report = await _db.ModerationReports
                .FirstOrDefaultAsync(x => x.ReportId == reportId);

            if (report == null)
            {
                return NotFound(new { message = "Report not found." });
            }

            var now = DateTime.UtcNow;
            var shouldHideMessage = dto.HideMessage ?? true;
            var shouldEjectUser = dto.EjectUser ?? true;
            long? hiddenClubId = null;
            long? hiddenMessageId = null;
            bool messageHidden = false;
            bool userEjected = false;
            int disabledMemberships = 0;

            if (shouldHideMessage && report.MessageId.HasValue)
            {
                var message = await _db.ClubMessages
                    .FirstOrDefaultAsync(x => x.MessageId == report.MessageId.Value);

                if (message != null)
                {
                    hiddenClubId = message.ClubId;
                    hiddenMessageId = message.MessageId;

                    if (!message.IsDeleted)
                    {
                        message.IsDeleted = true;
                        message.Content = null;
                        message.MediaUrl = null;
                        messageHidden = true;
                    }
                }
            }

            if (shouldEjectUser && report.TargetUserId.HasValue)
            {
                var user = await _db.Users
                    .FirstOrDefaultAsync(x => x.UserId == report.TargetUserId.Value);

                if (user != null)
                {
                    if (user.IsActive)
                    {
                        user.IsActive = false;
                        user.UpdatedAt = now;
                        userEjected = true;
                    }

                    var memberships = await _db.ClubMembers
                        .Where(x => x.UserId == user.UserId && x.IsActive)
                        .ToListAsync();

                    foreach (var membership in memberships)
                    {
                        membership.IsActive = false;
                    }

                    disabledMemberships = memberships.Count;
                }
            }

            report.Status = "RESOLVED";
            report.ReviewedByUserId = GetCurrentAdminUserId();
            report.ReviewedByName = GetCurrentAdminName();
            report.ReviewedAt ??= now;
            report.ResolvedAt = now;
            report.ResolutionAction = userEjected
                ? "USER_EJECTED"
                : messageHidden
                    ? "MESSAGE_HIDDEN"
                    : "NONE";
            report.ResolutionNote = BuildResolutionNote(dto.Note, messageHidden, userEjected, disabledMemberships);

            await _db.SaveChangesAsync();

            if (messageHidden && hiddenClubId.HasValue && hiddenMessageId.HasValue)
            {
                await _realtimeHub.SendClubMessageDeletedAsync(hiddenClubId.Value, hiddenMessageId.Value);
            }

            if (userEjected && report.TargetUserId.HasValue)
            {
                await _realtimeHub.DisconnectUserAsync(report.TargetUserId.Value.ToString(), "moderation_report_resolved");
            }

            return Ok(new
            {
                reportId = report.ReportId,
                status = report.Status,
                messageHidden,
                userEjected,
                disabledMemberships,
                resolutionAction = report.ResolutionAction,
                resolutionNote = report.ResolutionNote,
                resolvedAt = report.ResolvedAt
            });
        }

        [HttpPost("users/{userId:long}/eject")]
        public async Task<IActionResult> EjectUser(long userId, [FromBody] ModerationUserActionDto? dto)
        {
            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;

            var memberships = await _db.ClubMembers
                .Where(x => x.UserId == userId && x.IsActive)
                .ToListAsync();

            foreach (var membership in memberships)
            {
                membership.IsActive = false;
            }

            await _db.SaveChangesAsync();
            await _realtimeHub.DisconnectUserAsync(userId.ToString(), "moderation_eject");

            return Ok(new
            {
                userId = user.UserId,
                fullName = user.FullName,
                isActive = user.IsActive,
                disabledMemberships = memberships.Count,
                note = dto?.Note
            });
        }

        [HttpPost("users/{userId:long}/reinstate")]
        public async Task<IActionResult> ReinstateUser(long userId, [FromBody] ModerationUserActionDto? dto)
        {
            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                userId = user.UserId,
                fullName = user.FullName,
                isActive = user.IsActive,
                note = dto?.Note
            });
        }

        private long? GetCurrentAdminUserId()
        {
            var raw =
                User.FindFirstValue("uid") ??
                User.FindFirstValue("UserId") ??
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            return long.TryParse(raw, out var userId) ? userId : null;
        }

        private string GetCurrentAdminName()
        {
            return User.FindFirstValue("FullName") ??
                   User.FindFirstValue(ClaimTypes.Name) ??
                   User.Identity?.Name ??
                   "Admin";
        }

        private static string BuildResolutionNote(
            string? note,
            bool messageHidden,
            bool userEjected,
            int disabledMemberships)
        {
            var actions = new List<string>();

            if (messageHidden)
            {
                actions.Add("message hidden");
            }

            if (userEjected)
            {
                actions.Add($"user ejected; disabled memberships: {disabledMemberships}");
            }

            if (!string.IsNullOrWhiteSpace(note))
            {
                actions.Add(note.Trim());
            }

            return actions.Count == 0
                ? "Reviewed without additional action."
                : string.Join("; ", actions);
        }

        public class ModerationResolveReportDto
        {
            public bool? HideMessage { get; set; }
            public bool? EjectUser { get; set; }
            public string? Note { get; set; }
        }

        public class ModerationUserActionDto
        {
            public string? Note { get; set; }
        }
    }
}
