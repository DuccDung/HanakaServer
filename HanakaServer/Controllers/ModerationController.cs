using System.Net;
using System.Security.Claims;
using HanakaServer.Data;
using HanakaServer.Models;
using mail_service.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/moderation")]
    public class ModerationController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;

        public ModerationController(
            PickleballDbContext db,
            IEmailSender emailSender,
            IConfiguration config)
        {
            _db = db;
            _emailSender = emailSender;
            _config = config;
        }

        [HttpGet("reports")]
        public async Task<IActionResult> GetMyReports([FromQuery] int limit = 20, CancellationToken ct = default)
        {
            var reporterUserId = await TryGetCurrentUserIdAsync();
            if (!reporterUserId.HasValue)
            {
                return Unauthorized(new { message = "Authentication is required." });
            }

            if (limit < 1) limit = 20;
            if (limit > 100) limit = 100;

            var items = await _db.ModerationReports
                .AsNoTracking()
                .Where(x => x.ReporterUserId == reporterUserId.Value)
                .OrderByDescending(x => x.CreatedAt)
                .Take(limit)
                .Select(x => new
                {
                    reportId = x.ReportId,
                    kind = ToApiReportType(x.ReportType),
                    reason = ToApiReasonCode(x.ReasonCode),
                    reasonLabel = x.ReasonLabel,
                    notes = x.Notes,
                    clubId = x.ClubId,
                    messageId = x.MessageId,
                    messageContent = x.MessageContentSnapshot,
                    targetUserId = x.TargetUserId,
                    targetUserName = x.TargetUser != null
                        ? x.TargetUser.FullName
                        : x.TargetNameSnapshot,
                    createdAt = x.CreatedAt,
                    source = ToApiSource(x.Source),
                    reporterUserId = x.ReporterUserId,
                    status = ToApiStatus(x.Status),
                    developerNotified = x.DeveloperNotified,
                    developerNotifiedAt = x.DeveloperNotifiedAt,
                    resolutionAction = ToApiResolutionAction(x.ResolutionAction),
                    reviewedAt = x.ReviewedAt,
                    resolvedAt = x.ResolvedAt,
                    pendingSync = false,
                    syncedRemote = true
                })
                .ToListAsync(ct);

            return Ok(new
            {
                ok = true,
                items
            });
        }

        [HttpGet("blocks")]
        public async Task<IActionResult> GetMyBlocks(CancellationToken ct)
        {
            var blockerUserId = await TryGetCurrentUserIdAsync();
            if (!blockerUserId.HasValue)
            {
                return Unauthorized(new { message = "Authentication is required." });
            }

            var items = await _db.UserBlocks
                .AsNoTracking()
                .Where(x => x.BlockerUserId == blockerUserId.Value && x.IsActive)
                .OrderByDescending(x => x.BlockedAt)
                .Select(x => new
                {
                    blockId = x.BlockId,
                    reportId = x.ReportId,
                    userId = x.BlockedUserId,
                    fullName = x.BlockedUser.FullName,
                    clubId = x.SourceClubId,
                    messageId = x.SourceMessageId,
                    reason = ToApiReasonCode(x.ReasonCode),
                    notes = x.Notes,
                    source = ToApiSource(x.Source),
                    blockedAt = x.BlockedAt,
                    pendingSync = false,
                    syncedRemote = true
                })
                .ToListAsync(ct);

            return Ok(new
            {
                ok = true,
                items
            });
        }

        [HttpPost("reports")]
        public async Task<IActionResult> SubmitReport([FromBody] ModerationReportRequestDto? dto, CancellationToken ct)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Missing report payload." });
            }

            var reporterUserId = await TryGetCurrentUserIdAsync();
            var reportSource = NormalizeReportSource(dto.Source);
            var isDemoSource = IsDemoSource(reportSource);

            if (!reporterUserId.HasValue)
            {
                if (!isDemoSource)
                {
                    return Unauthorized(new { message = "Authentication is required." });
                }

                return Ok(new
                {
                    ok = true,
                    developerNotified = true,
                    item = BuildEphemeralReportItem(dto, null, null, null, reportSource)
                });
            }

            var reporter = await LoadUserBriefAsync(reporterUserId.Value, ct);
            if (reporter == null || !reporter.IsActive)
            {
                return NotFound(new { message = "Reporter user was not found." });
            }

            var messageContext = dto.MessageId.HasValue && dto.MessageId.Value > 0
                ? await LoadMessageContextAsync(dto.MessageId.Value, ct)
                : null;

            var targetUserId = dto.TargetUserId;
            if (!targetUserId.HasValue && messageContext?.SenderUserId > 0)
            {
                targetUserId = messageContext.SenderUserId;
            }

            if (!targetUserId.HasValue && messageContext == null)
            {
                return BadRequest(new { message = "A report must reference a user or a message." });
            }

            var targetUser = targetUserId.HasValue
                ? await LoadUserBriefAsync(targetUserId.Value, ct)
                : null;

            long? clubId = dto.ClubId ?? messageContext?.ClubId;
            long? messageId = messageContext?.MessageId;
            var messageContent = Clean(dto.MessageContent, 2000) ?? messageContext?.Content;

            var reportEntity = new ModerationReport
            {
                ReporterUserId = reporter.UserId,
                TargetUserId = targetUser?.UserId,
                ClubId = clubId,
                MessageId = messageId,
                ReportType = NormalizeReportType(dto.Kind),
                ReasonCode = NormalizeReasonCode(dto.Reason),
                ReasonLabel = Clean(dto.ReasonLabel, 150) ?? BuildReasonLabel(dto.Reason),
                Notes = Clean(dto.Notes, 1000),
                MessageContentSnapshot = messageContent,
                ReporterNameSnapshot = reporter.FullName,
                TargetNameSnapshot = targetUser?.FullName ?? Clean(dto.TargetUserName, 150),
                Source = reportSource,
                Status = "PENDING",
                DeveloperNotified = true,
                DeveloperNotifiedAt = DateTime.UtcNow,
                ResolutionAction = "NONE",
                SlaDueAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow
            };

            _db.ModerationReports.Add(reportEntity);
            await _db.SaveChangesAsync(ct);

            QueueReportEmail(reportEntity, reporter, targetUser);

            return Ok(new
            {
                ok = true,
                developerNotified = true,
                item = BuildSavedReportItem(reportEntity, targetUser)
            });
        }

        [HttpPost("blocks")]
        public async Task<IActionResult> SubmitBlock([FromBody] ModerationBlockRequestDto? dto, CancellationToken ct)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Missing block payload." });
            }

            var blockerUserId = await TryGetCurrentUserIdAsync();
            var blockSource = NormalizeBlockSource(dto.Source);
            var reportSource = NormalizeReportSource(dto.Source);
            var isDemoSource = IsDemoSource(reportSource);

            if (!blockerUserId.HasValue)
            {
                if (!isDemoSource)
                {
                    return Unauthorized(new { message = "Authentication is required." });
                }

                return Ok(new
                {
                    ok = true,
                    developerNotified = true,
                    item = BuildEphemeralBlockItem(dto, blockSource)
                });
            }

            var blocker = await LoadUserBriefAsync(blockerUserId.Value, ct);
            if (blocker == null || !blocker.IsActive)
            {
                return NotFound(new { message = "Blocking user was not found." });
            }

            var messageContext = dto.MessageId.HasValue && dto.MessageId.Value > 0
                ? await LoadMessageContextAsync(dto.MessageId.Value, ct)
                : null;

            var blockedUserId = dto.UserId;
            if (!blockedUserId.HasValue && messageContext?.SenderUserId > 0)
            {
                blockedUserId = messageContext.SenderUserId;
            }

            if (!blockedUserId.HasValue || blockedUserId.Value <= 0)
            {
                return BadRequest(new { message = "Missing userId to block." });
            }

            if (blockedUserId.Value == blocker.UserId)
            {
                return BadRequest(new { message = "You cannot block yourself." });
            }

            var blockedUser = await LoadUserBriefAsync(blockedUserId.Value, ct);
            if (blockedUser == null)
            {
                if (!isDemoSource)
                {
                    return NotFound(new { message = "Blocked user was not found." });
                }

                return Ok(new
                {
                    ok = true,
                    developerNotified = true,
                    item = BuildEphemeralBlockItem(dto, blockSource)
                });
            }

            var existingActiveBlock = await _db.UserBlocks
                .AsNoTracking()
                .Where(x =>
                    x.BlockerUserId == blocker.UserId &&
                    x.BlockedUserId == blockedUser.UserId &&
                    x.IsActive)
                .Select(x => x.BlockId)
                .FirstOrDefaultAsync(ct);

            if (existingActiveBlock > 0)
            {
                return Ok(new
                {
                    ok = true,
                    developerNotified = true,
                    item = await BuildPersistedBlockItemAsync(existingActiveBlock, ct)
                });
            }

            var reasonCode = NormalizeReasonCode(dto.Reason);
            var now = DateTime.UtcNow;
            var clubId = dto.ClubId ?? messageContext?.ClubId;
            var messageId = messageContext?.MessageId;

            var reportEntity = new ModerationReport
            {
                ReporterUserId = blocker.UserId,
                TargetUserId = blockedUser.UserId,
                ClubId = clubId,
                MessageId = messageId,
                ReportType = "USER",
                ReasonCode = reasonCode,
                ReasonLabel = BuildReasonLabel(dto.Reason),
                Notes = "User was blocked in the app and should be reviewed by moderators.",
                MessageContentSnapshot = messageContext?.Content,
                ReporterNameSnapshot = blocker.FullName,
                TargetNameSnapshot = blockedUser.FullName,
                Source = "BLOCK_ACTION",
                Status = "PENDING",
                DeveloperNotified = true,
                DeveloperNotifiedAt = now,
                ResolutionAction = "NONE",
                SlaDueAt = now.AddHours(24),
                CreatedAt = now
            };

            var blockEntity = new UserBlock
            {
                BlockerUserId = blocker.UserId,
                BlockedUserId = blockedUser.UserId,
                SourceClubId = clubId,
                SourceMessageId = messageId,
                Report = reportEntity,
                ReasonCode = reasonCode,
                Notes = Clean(dto.Notes, 500),
                Source = blockSource,
                IsActive = true,
                BlockedAt = now
            };

            _db.ModerationReports.Add(reportEntity);
            _db.UserBlocks.Add(blockEntity);
            await _db.SaveChangesAsync(ct);

            QueueBlockEmail(reportEntity, blocker, blockedUser, blockEntity);

            return Ok(new
            {
                ok = true,
                developerNotified = true,
                item = BuildSavedBlockItem(blockEntity, blockedUser)
            });
        }

        [HttpDelete("blocks/{blockedUserId:long}")]
        public async Task<IActionResult> RemoveBlock(long blockedUserId, CancellationToken ct)
        {
            var blockerUserId = await TryGetCurrentUserIdAsync();
            if (!blockerUserId.HasValue)
            {
                return Unauthorized(new { message = "Authentication is required." });
            }

            var entity = await _db.UserBlocks
                .FirstOrDefaultAsync(x =>
                    x.BlockerUserId == blockerUserId.Value &&
                    x.BlockedUserId == blockedUserId &&
                    x.IsActive, ct);

            if (entity == null)
            {
                return Ok(new
                {
                    ok = true,
                    blockedUserId,
                    removed = false
                });
            }

            entity.IsActive = false;
            entity.UnblockedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                ok = true,
                blockedUserId,
                removed = true
            });
        }

        private async Task<long?> TryGetCurrentUserIdAsync()
        {
            var directClaim =
                User.FindFirstValue("uid") ??
                User.FindFirstValue("UserId") ??
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (long.TryParse(directClaim, out var directUserId))
            {
                return directUserId;
            }

            var authResult = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (!authResult.Succeeded || authResult.Principal == null)
            {
                return null;
            }

            var bearerClaim =
                authResult.Principal.FindFirstValue("uid") ??
                authResult.Principal.FindFirstValue("UserId") ??
                authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

            return long.TryParse(bearerClaim, out var bearerUserId)
                ? bearerUserId
                : null;
        }

        private async Task<UserBriefDto?> LoadUserBriefAsync(long userId, CancellationToken ct)
        {
            return await _db.Users
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => new UserBriefDto
                {
                    UserId = x.UserId,
                    FullName = x.FullName,
                    Email = x.Email,
                    IsActive = x.IsActive
                })
                .FirstOrDefaultAsync(ct);
        }

        private async Task<MessageContextDto?> LoadMessageContextAsync(long messageId, CancellationToken ct)
        {
            return await _db.ClubMessages
                .AsNoTracking()
                .Where(x => x.MessageId == messageId)
                .Select(x => new MessageContextDto
                {
                    MessageId = x.MessageId,
                    ClubId = x.ClubId,
                    SenderUserId = x.SenderUserId,
                    Content = x.Content
                })
                .FirstOrDefaultAsync(ct);
        }

        private async Task<object?> BuildPersistedReportItemAsync(long reportId, CancellationToken ct)
        {
            return await _db.ModerationReports
                .AsNoTracking()
                .Where(x => x.ReportId == reportId)
                .Select(x => new
                {
                    reportId = x.ReportId,
                    kind = ToApiReportType(x.ReportType),
                    reason = ToApiReasonCode(x.ReasonCode),
                    reasonLabel = x.ReasonLabel,
                    notes = x.Notes,
                    clubId = x.ClubId,
                    messageId = x.MessageId,
                    messageContent = x.MessageContentSnapshot,
                    targetUserId = x.TargetUserId,
                    targetUserName = x.TargetUser != null
                        ? x.TargetUser.FullName
                        : x.TargetNameSnapshot,
                    createdAt = x.CreatedAt,
                    source = ToApiSource(x.Source),
                    reporterUserId = x.ReporterUserId,
                    status = ToApiStatus(x.Status),
                    developerNotified = x.DeveloperNotified,
                    developerNotifiedAt = x.DeveloperNotifiedAt,
                    pendingSync = false,
                    syncedRemote = true
                })
                .FirstOrDefaultAsync(ct);
        }

        private async Task<object?> BuildPersistedBlockItemAsync(long blockId, CancellationToken ct)
        {
            return await _db.UserBlocks
                .AsNoTracking()
                .Where(x => x.BlockId == blockId)
                .Select(x => new
                {
                    blockId = x.BlockId,
                    reportId = x.ReportId,
                    userId = x.BlockedUserId,
                    fullName = x.BlockedUser.FullName,
                    clubId = x.SourceClubId,
                    messageId = x.SourceMessageId,
                    reason = ToApiReasonCode(x.ReasonCode),
                    notes = x.Notes,
                    source = ToApiSource(x.Source),
                    blockedAt = x.BlockedAt,
                    pendingSync = false,
                    syncedRemote = true
                })
                .FirstOrDefaultAsync(ct);
        }

        private object BuildSavedReportItem(ModerationReport report, UserBriefDto? targetUser)
        {
            return new
            {
                reportId = report.ReportId,
                kind = ToApiReportType(report.ReportType),
                reason = ToApiReasonCode(report.ReasonCode),
                reasonLabel = report.ReasonLabel,
                notes = report.Notes,
                clubId = report.ClubId,
                messageId = report.MessageId,
                messageContent = report.MessageContentSnapshot,
                targetUserId = report.TargetUserId,
                targetUserName = targetUser?.FullName ?? report.TargetNameSnapshot,
                createdAt = report.CreatedAt,
                source = ToApiSource(report.Source),
                reporterUserId = report.ReporterUserId,
                status = ToApiStatus(report.Status),
                developerNotified = report.DeveloperNotified,
                developerNotifiedAt = report.DeveloperNotifiedAt,
                pendingSync = false,
                syncedRemote = true
            };
        }

        private object BuildSavedBlockItem(UserBlock block, UserBriefDto blockedUser)
        {
            return new
            {
                blockId = block.BlockId,
                reportId = block.ReportId,
                userId = block.BlockedUserId,
                fullName = blockedUser.FullName,
                clubId = block.SourceClubId,
                messageId = block.SourceMessageId,
                reason = ToApiReasonCode(block.ReasonCode),
                notes = block.Notes,
                source = ToApiSource(block.Source),
                blockedAt = block.BlockedAt,
                pendingSync = false,
                syncedRemote = true
            };
        }

        private object BuildEphemeralReportItem(
            ModerationReportRequestDto dto,
            UserBriefDto? reporter,
            UserBriefDto? targetUser,
            MessageContextDto? messageContext,
            string reportSource)
        {
            return new
            {
                reportId = $"demo-{Guid.NewGuid():N}"[..17],
                kind = NormalizeReportType(dto.Kind).Equals("USER", StringComparison.Ordinal)
                    ? "user"
                    : "message",
                reason = ToApiReasonCode(NormalizeReasonCode(dto.Reason)),
                reasonLabel = Clean(dto.ReasonLabel, 150) ?? BuildReasonLabel(dto.Reason),
                notes = Clean(dto.Notes, 1000),
                clubId = dto.ClubId ?? messageContext?.ClubId,
                messageId = dto.MessageId ?? messageContext?.MessageId,
                messageContent = Clean(dto.MessageContent, 2000) ?? messageContext?.Content,
                targetUserId = dto.TargetUserId ?? targetUser?.UserId ?? messageContext?.SenderUserId,
                targetUserName = targetUser?.FullName ?? Clean(dto.TargetUserName, 150),
                createdAt = DateTime.UtcNow,
                source = ToApiSource(reportSource),
                reporterUserId = reporter?.UserId,
                status = "submitted",
                developerNotified = true,
                pendingSync = false,
                syncedRemote = false
            };
        }

        private object BuildEphemeralBlockItem(ModerationBlockRequestDto dto, string blockSource)
        {
            return new
            {
                blockId = (long?)null,
                reportId = (long?)null,
                userId = dto.UserId,
                fullName = Clean(dto.FullName, 150) ?? "User",
                clubId = dto.ClubId,
                messageId = dto.MessageId,
                reason = ToApiReasonCode(NormalizeReasonCode(dto.Reason)),
                notes = Clean(dto.Notes, 500),
                source = ToApiSource(blockSource),
                blockedAt = DateTime.UtcNow,
                pendingSync = false,
                syncedRemote = false
            };
        }

        private async Task TrySendReportEmailAsync(
            ModerationReport report,
            UserBriefDto reporter,
            UserBriefDto? targetUser,
            CancellationToken ct)
        {
            try
            {
                await _emailSender.SendAsync(
                    GetSupportInboxAddress(),
                    $"[Hanaka Sport Moderation] Report #{report.ReportId}",
                    BuildReportEmailHtml(report, reporter, targetUser),
                    reporter.Email,
                    ct);
            }
            catch
            {
                // Persistence is the moderation source of truth.
            }
        }

        private async Task TrySendBlockEmailAsync(
            ModerationReport report,
            UserBriefDto blocker,
            UserBriefDto blockedUser,
            UserBlock block,
            CancellationToken ct)
        {
            try
            {
                await _emailSender.SendAsync(
                    GetSupportInboxAddress(),
                    $"[Hanaka Sport Moderation] Block #{block.BlockId}",
                    BuildBlockEmailHtml(report, blocker, blockedUser, block),
                    blocker.Email,
                    ct);
            }
            catch
            {
                // Persistence is the moderation source of truth.
            }
        }

        private void QueueReportEmail(
            ModerationReport report,
            UserBriefDto reporter,
            UserBriefDto? targetUser)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await TrySendReportEmailAsync(
                        report,
                        reporter,
                        targetUser,
                        CancellationToken.None);
                }
                catch
                {
                    // Best-effort email only.
                }
            });
        }

        private void QueueBlockEmail(
            ModerationReport report,
            UserBriefDto blocker,
            UserBriefDto blockedUser,
            UserBlock block)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await TrySendBlockEmailAsync(
                        report,
                        blocker,
                        blockedUser,
                        block,
                        CancellationToken.None);
                }
                catch
                {
                    // Best-effort email only.
                }
            });
        }

        private string GetSupportInboxAddress()
        {
            return _config["Support:InboxAddress"]
                ?? _config["Email:FromAddress"]
                ?? "duccdung999@gmail.com";
        }

        private static string NormalizeReportType(string? kind)
        {
            var normalized = Clean(kind, 20)?.Replace("-", "_").Replace(" ", "_").ToUpperInvariant();
            return normalized == "USER" ? "USER" : "MESSAGE";
        }

        private static string NormalizeReasonCode(string? reason)
        {
            var normalized = Clean(reason, 30)?.Replace("-", "_").Replace(" ", "_").ToUpperInvariant();
            return normalized switch
            {
                "HATE_OR_HARASSMENT" => "HATE_OR_HARASSMENT",
                "VIOLENT_THREAT" => "VIOLENT_THREAT",
                "SEXUAL_CONTENT" => "SEXUAL_CONTENT",
                "SPAM_OR_SCAM" => "SPAM_OR_SCAM",
                _ => "OTHER"
            };
        }

        private static string NormalizeReportSource(string? source)
        {
            var normalized = Clean(source, 30)?.Replace("-", "_").Replace(" ", "_").ToUpperInvariant();
            return string.IsNullOrWhiteSpace(normalized) ? "APP" : normalized;
        }

        private static string NormalizeBlockSource(string? source)
        {
            var normalized = NormalizeReportSource(source);

            if (normalized.StartsWith("CHAT", StringComparison.Ordinal))
                return "CHAT";

            if (normalized.StartsWith("PROFILE", StringComparison.Ordinal))
                return "PROFILE";

            if (normalized.StartsWith("ADMIN", StringComparison.Ordinal))
                return "ADMIN";

            return "SYSTEM";
        }

        private static bool IsDemoSource(string? source)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   source.StartsWith("DEMO_", StringComparison.Ordinal);
        }

        private static string ToApiReportType(string? reportType)
        {
            return string.Equals(reportType, "USER", StringComparison.OrdinalIgnoreCase)
                ? "user"
                : "message";
        }

        private static string ToApiReasonCode(string? reasonCode)
        {
            return (reasonCode ?? "OTHER").Trim().ToLowerInvariant();
        }

        private static string ToApiSource(string? source)
        {
            return (source ?? "APP").Trim().ToLowerInvariant();
        }

        private static string ToApiStatus(string? status)
        {
            return (status ?? "PENDING").Trim().ToLowerInvariant();
        }

        private static string ToApiResolutionAction(string? action)
        {
            return (action ?? "NONE").Trim().ToLowerInvariant();
        }

        private static string BuildReasonLabel(string? reason)
        {
            return NormalizeReasonCode(reason) switch
            {
                "HATE_OR_HARASSMENT" => "Harassment or hate speech",
                "VIOLENT_THREAT" => "Violent threat",
                "SEXUAL_CONTENT" => "Sexual or explicit content",
                "SPAM_OR_SCAM" => "Spam or scam",
                _ => "Other"
            };
        }

        private static string? Clean(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength
                ? trimmed
                : trimmed[..maxLength];
        }

        private static string HtmlEncode(string? value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }

        private static string BuildReportEmailHtml(
            ModerationReport report,
            UserBriefDto reporter,
            UserBriefDto? targetUser)
        {
            var targetName = targetUser?.FullName ?? report.TargetNameSnapshot ?? "Unknown";
            return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8" />
                <title>Moderation Report</title>
            </head>
            <body style="margin:0;padding:24px;background:#f8fafc;font-family:Arial,sans-serif;color:#0f172a;">
                <div style="max-width:760px;margin:0 auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:18px;padding:24px;">
                    <div style="display:inline-block;background:#dbeafe;color:#1d4ed8;padding:8px 14px;border-radius:999px;font-size:12px;font-weight:700;">
                        Hanaka Sport Moderation
                    </div>
                    <h2 style="margin:16px 0 8px;">Community report received</h2>
                    <p style="margin:0 0 20px;line-height:1.6;">
                        Report <strong>#{HtmlEncode(report.ReportId.ToString())}</strong> should be reviewed within 24 hours.
                    </p>

                    <table style="width:100%;border-collapse:collapse;">
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;width:220px;">Created at (UTC)</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(report.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"))}</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Source</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(report.Source)}</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Report type</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(report.ReportType)}</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Reason</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(report.ReasonLabel ?? report.ReasonCode)}</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Reporter</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(reporter.FullName)} (ID: {HtmlEncode(reporter.UserId.ToString())}, Email: {HtmlEncode(reporter.Email)})</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Target user</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(targetName)} (ID: {HtmlEncode(report.TargetUserId?.ToString())})</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Club ID</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(report.ClubId?.ToString())}</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Message ID</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(report.MessageId?.ToString())}</td></tr>
                    </table>

                    <div style="margin-top:20px;">
                        <div style="font-weight:700;margin-bottom:8px;">Message snapshot</div>
                        <div style="padding:14px;border-radius:12px;background:#f8fafc;border:1px solid #e2e8f0;line-height:1.6;">{HtmlEncode(report.MessageContentSnapshot ?? "No snapshot attached.")}</div>
                    </div>

                    <div style="margin-top:20px;">
                        <div style="font-weight:700;margin-bottom:8px;">Reporter notes</div>
                        <div style="padding:14px;border-radius:12px;background:#f8fafc;border:1px solid #e2e8f0;line-height:1.6;">{HtmlEncode(report.Notes ?? "No notes.")}</div>
                    </div>
                </div>
            </body>
            </html>
            """;
        }

        private static string BuildBlockEmailHtml(
            ModerationReport report,
            UserBriefDto blocker,
            UserBriefDto blockedUser,
            UserBlock block)
        {
            return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8" />
                <title>Moderation Block</title>
            </head>
            <body style="margin:0;padding:24px;background:#f8fafc;font-family:Arial,sans-serif;color:#0f172a;">
                <div style="max-width:760px;margin:0 auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:18px;padding:24px;">
                    <div style="display:inline-block;background:#fee2e2;color:#b91c1c;padding:8px 14px;border-radius:999px;font-size:12px;font-weight:700;">
                        Hanaka Sport Block Alert
                    </div>
                    <h2 style="margin:16px 0 8px;">User blocked in app</h2>
                    <p style="margin:0 0 20px;line-height:1.6;">
                        Block <strong>#{HtmlEncode(block.BlockId.ToString())}</strong> and linked report <strong>#{HtmlEncode(report.ReportId.ToString())}</strong> should be reviewed by moderators.
                    </p>

                    <table style="width:100%;border-collapse:collapse;">
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;width:220px;">Blocked at (UTC)</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(block.BlockedAt.ToString("yyyy-MM-dd HH:mm:ss"))}</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Source</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(block.Source)}</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Reason</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(report.ReasonLabel ?? block.ReasonCode)}</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Blocker</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(blocker.FullName)} (ID: {HtmlEncode(blocker.UserId.ToString())}, Email: {HtmlEncode(blocker.Email)})</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Blocked user</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(blockedUser.FullName)} (ID: {HtmlEncode(blockedUser.UserId.ToString())})</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Club ID</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(block.SourceClubId?.ToString())}</td></tr>
                        <tr><td style="padding:10px;border:1px solid #e2e8f0;background:#f8fafc;font-weight:700;">Message ID</td><td style="padding:10px;border:1px solid #e2e8f0;">{HtmlEncode(block.SourceMessageId?.ToString())}</td></tr>
                    </table>

                    <div style="margin-top:20px;">
                        <div style="font-weight:700;margin-bottom:8px;">Moderator note</div>
                        <div style="padding:14px;border-radius:12px;background:#f8fafc;border:1px solid #e2e8f0;line-height:1.6;">{HtmlEncode(report.Notes ?? "No note attached.")}</div>
                    </div>
                </div>
            </body>
            </html>
            """;
        }

        public class ModerationReportRequestDto
        {
            public string? Kind { get; set; }
            public string? Reason { get; set; }
            public string? ReasonLabel { get; set; }
            public string? Notes { get; set; }
            public long? ClubId { get; set; }
            public long? MessageId { get; set; }
            public string? MessageContent { get; set; }
            public long? TargetUserId { get; set; }
            public string? TargetUserName { get; set; }
            public string? Source { get; set; }
        }

        public class ModerationBlockRequestDto
        {
            public long? ClubId { get; set; }
            public long? UserId { get; set; }
            public string? FullName { get; set; }
            public string? Reason { get; set; }
            public long? MessageId { get; set; }
            public string? Notes { get; set; }
            public string? Source { get; set; }
        }

        private class UserBriefDto
        {
            public long UserId { get; set; }
            public string? FullName { get; set; }
            public string? Email { get; set; }
            public bool IsActive { get; set; }
        }

        private class MessageContextDto
        {
            public long MessageId { get; set; }
            public long ClubId { get; set; }
            public long SenderUserId { get; set; }
            public string? Content { get; set; }
        }
    }
}
