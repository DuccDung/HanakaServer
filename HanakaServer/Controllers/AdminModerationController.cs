using HanakaServer.Data;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        public class ModerationUserActionDto
        {
            public string? Note { get; set; }
        }
    }
}
