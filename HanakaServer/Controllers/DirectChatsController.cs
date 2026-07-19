using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Route("api/direct-chats")]
    public class DirectChatsController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;
        private readonly RealtimeHub _realtimeHub;

        private static readonly string[] UnsafeChatTerms =
        {
            "fuck",
            "shit",
            "bitch",
            "asshole",
            "motherfucker",
            "kill",
            "rape",
            "sex",
            "nude",
            "xxx"
        };

        public DirectChatsController(
            PickleballDbContext db,
            IConfiguration config,
            RealtimeHub realtimeHub)
        {
            _db = db;
            _config = config;
            _realtimeHub = realtimeHub;
        }

        [HttpGet("users/search")]
        public async Task<IActionResult> SearchUsers(
            [FromQuery] string? keyword,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 50) pageSize = 50;

            var userId = GetUserIdFromToken();
            var k = (keyword ?? string.Empty).Trim();
            var isNumeric = long.TryParse(k, out var numericId);

            if (k.Length < 2 && !isNumeric)
            {
                return BadRequest(new { message = "Nhập ít nhất 2 ký tự hoặc nhập đúng ID người dùng." });
            }

            var q = _db.Users
                .AsNoTracking()
                .Where(x => x.IsActive && x.UserId != userId);

            q = q.Where(x =>
                x.FullName.Contains(k) ||
                (x.Phone != null && x.Phone.Contains(k)) ||
                (isNumeric && x.UserId == numericId) ||
                x.UserId.ToString().Contains(k));

            var total = await q.CountAsync(ct);

            var users = await q
                .OrderByDescending(x => isNumeric && x.UserId == numericId)
                .ThenBy(x => x.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.UserId,
                    x.FullName,
                    x.Phone,
                    x.City,
                    x.Gender,
                    x.Verified,
                    x.AvatarUrl
                })
                .ToListAsync(ct);

            var targetIds = users.Select(x => x.UserId).ToList();
            var blocks = await LoadBlockStatesAsync(userId, targetIds, ct);

            var existingRooms = await _db.DirectChatRooms
                .AsNoTracking()
                .Where(x =>
                    (x.User1Id == userId && targetIds.Contains(x.User2Id)) ||
                    (x.User2Id == userId && targetIds.Contains(x.User1Id)))
                .Select(x => new { x.DirectChatRoomId, x.User1Id, x.User2Id })
                .ToListAsync(ct);

            var items = users.Select(x =>
            {
                var pair = NormalizePair(userId, x.UserId);
                var existingRoom = existingRooms.FirstOrDefault(room =>
                    room.User1Id == pair.User1Id &&
                    room.User2Id == pair.User2Id);

                var state = GetBlockState(blocks, userId, x.UserId);

                return new
                {
                    userId = x.UserId,
                    fullName = x.FullName,
                    phone = MaskPhone(x.Phone),
                    city = x.City,
                    gender = x.Gender,
                    verified = x.Verified,
                    avatarUrl = ToAbsoluteUrl(x.AvatarUrl),
                    existingRoomId = existingRoom?.DirectChatRoomId,
                    isBlockedByMe = state.IsBlockedByMe,
                    hasBlockedMe = state.HasBlockedMe,
                    canChat = !state.IsBlockedByMe && !state.HasBlockedMe
                };
            });

            return Ok(new
            {
                total,
                page,
                pageSize,
                items
            });
        }

        [HttpGet("rooms")]
        public async Task<IActionResult> GetRooms(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 50) pageSize = 50;

            var userId = GetUserIdFromToken();

            var q = _db.DirectChatRooms
                .AsNoTracking()
                .Include(x => x.User1)
                .Include(x => x.User2)
                .Include(x => x.LastMessage)
                    .ThenInclude(x => x!.SenderUser)
                .Where(x => x.IsActive && (x.User1Id == userId || x.User2Id == userId));

            var total = await q.CountAsync(ct);

            var rooms = await q
                .OrderByDescending(x => x.LastMessageAt ?? x.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var roomIds = rooms.Select(x => x.DirectChatRoomId).ToList();
            var otherUserIds = rooms.Select(x => GetOtherUserId(x, userId)).ToList();
            var blocks = await LoadBlockStatesAsync(userId, otherUserIds, ct);

            var participants = await _db.DirectChatRoomParticipants
                .AsNoTracking()
                .Where(x => x.UserId == userId && roomIds.Contains(x.DirectChatRoomId))
                .ToDictionaryAsync(x => x.DirectChatRoomId, ct);

            var unreadMap = new Dictionary<long, int>();
            foreach (var room in rooms)
            {
                var lastReadMessageId = participants.TryGetValue(room.DirectChatRoomId, out var participant)
                    ? participant.LastReadMessageId ?? 0
                    : 0;

                unreadMap[room.DirectChatRoomId] = await _db.DirectChatMessages
                    .AsNoTracking()
                    .CountAsync(x =>
                        x.DirectChatRoomId == room.DirectChatRoomId &&
                        x.DirectChatMessageId > lastReadMessageId &&
                        x.SenderUserId != userId &&
                        !x.IsDeleted,
                        ct);
            }

            return Ok(new
            {
                total,
                page,
                pageSize,
                items = rooms.Select(room => BuildRoomItem(room, userId, blocks, unreadMap))
            });
        }

        [HttpPost("rooms")]
        public async Task<IActionResult> CreateRoom(
            [FromBody] CreateDirectChatRoomRequestDto? req,
            CancellationToken ct)
        {
            var userId = GetUserIdFromToken();

            if (req == null || req.TargetUserId <= 0)
            {
                return BadRequest(new { message = "Thiếu người nhận tin nhắn." });
            }

            if (req.TargetUserId == userId)
            {
                return BadRequest(new { message = "Bạn không thể tự nhắn tin cho chính mình." });
            }

            var targetExists = await _db.Users
                .AsNoTracking()
                .AnyAsync(x => x.UserId == req.TargetUserId && x.IsActive, ct);

            if (!targetExists)
            {
                return NotFound(new { message = "Không tìm thấy người dùng." });
            }

            var pair = NormalizePair(userId, req.TargetUserId);

            var existingRoom = await LoadRoomWithUsersAsync(pair.User1Id, pair.User2Id, ct);
            if (existingRoom != null)
            {
                var blocks = await LoadBlockStatesAsync(userId, new[] { req.TargetUserId }, ct);
                return Ok(new
                {
                    message = "Đã mở cuộc trò chuyện.",
                    item = BuildRoomItem(existingRoom, userId, blocks, new Dictionary<long, int>())
                });
            }

            var hasBlock = await HasActiveBlockEitherDirectionAsync(userId, req.TargetUserId, ct);
            if (hasBlock)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Không thể tạo cuộc trò chuyện vì một trong hai tài khoản đang chặn người còn lại."
                });
            }

            var now = DateTime.UtcNow;
            var room = new DirectChatRoom
            {
                User1Id = pair.User1Id,
                User2Id = pair.User2Id,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.DirectChatRooms.Add(room);
            await _db.SaveChangesAsync(ct);

            _db.DirectChatRoomParticipants.AddRange(
                new DirectChatRoomParticipant
                {
                    DirectChatRoomId = room.DirectChatRoomId,
                    UserId = userId,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new DirectChatRoomParticipant
                {
                    DirectChatRoomId = room.DirectChatRoomId,
                    UserId = req.TargetUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                });

            await _db.SaveChangesAsync(ct);

            var savedRoom = await LoadRoomByIdWithUsersAsync(room.DirectChatRoomId, ct);
            var emptyBlocks = new List<UserBlock>();

            return Ok(new
            {
                message = "Tạo cuộc trò chuyện thành công.",
                item = BuildRoomItem(savedRoom!, userId, emptyBlocks, new Dictionary<long, int>())
            });
        }

        [HttpGet("rooms/{roomId:long}/messages")]
        public async Task<IActionResult> GetMessages(
            long roomId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30,
            CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 30;
            if (pageSize > 100) pageSize = 100;

            var userId = GetUserIdFromToken();
            var room = await LoadRoomByIdWithUsersAsync(roomId, ct);
            if (room == null || !IsRoomMember(room, userId))
            {
                return NotFound(new { message = "Không tìm thấy cuộc trò chuyện." });
            }

            var total = await _db.DirectChatMessages
                .AsNoTracking()
                .CountAsync(x => x.DirectChatRoomId == roomId && !x.IsDeleted, ct);

            var messages = await _db.DirectChatMessages
                .AsNoTracking()
                .Where(x => x.DirectChatRoomId == roomId && !x.IsDeleted)
                .Include(x => x.SenderUser)
                .Include(x => x.ReplyToMessage)
                .OrderByDescending(x => x.SentAt)
                .ThenByDescending(x => x.DirectChatMessageId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var latestMessageId = messages.Count > 0
                ? messages.Max(x => x.DirectChatMessageId)
                : await _db.DirectChatMessages
                    .AsNoTracking()
                    .Where(x => x.DirectChatRoomId == roomId && !x.IsDeleted)
                    .MaxAsync(x => (long?)x.DirectChatMessageId, ct);

            if (latestMessageId.HasValue)
            {
                await MarkRoomReadAsync(roomId, userId, latestMessageId.Value, ct);
            }

            var otherUserId = GetOtherUserId(room, userId);
            var blocks = await LoadBlockStatesAsync(userId, new[] { otherUserId }, ct);
            var state = GetBlockState(blocks, userId, otherUserId);

            return Ok(new
            {
                roomId,
                directChatRoomId = roomId,
                total,
                page,
                pageSize,
                isBlockedByMe = state.IsBlockedByMe,
                hasBlockedMe = state.HasBlockedMe,
                canSend = !state.IsBlockedByMe && !state.HasBlockedMe,
                items = messages
                    .OrderBy(x => x.SentAt)
                    .ThenBy(x => x.DirectChatMessageId)
                    .Select(BuildMessageItem)
            });
        }

        [HttpPost("rooms/{roomId:long}/messages")]
        public async Task<IActionResult> SendMessage(
            long roomId,
            [FromBody] SendDirectChatMessageRequestDto? req,
            CancellationToken ct)
        {
            var userId = GetUserIdFromToken();
            var room = await _db.DirectChatRooms
                .FirstOrDefaultAsync(x => x.DirectChatRoomId == roomId && x.IsActive, ct);

            if (room == null || !IsRoomMember(room, userId))
            {
                return NotFound(new { message = "Không tìm thấy cuộc trò chuyện." });
            }

            var otherUserId = GetOtherUserId(room, userId);
            var blocks = await LoadBlockStatesAsync(userId, new[] { otherUserId }, ct);
            var state = GetBlockState(blocks, userId, otherUserId);
            if (state.IsBlockedByMe || state.HasBlockedMe)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = state.IsBlockedByMe
                        ? "Bạn đã chặn người này. Bỏ chặn để tiếp tục nhắn tin."
                        : "Người này hiện không thể nhận tin nhắn từ bạn.",
                    isBlockedByMe = state.IsBlockedByMe,
                    hasBlockedMe = state.HasBlockedMe
                });
            }

            var messageType = NormalizeMessageType(req?.MessageType);
            if (messageType != "text" && messageType != "image")
            {
                return BadRequest(new { message = "MessageType chỉ hỗ trợ text hoặc image." });
            }

            var content = string.IsNullOrWhiteSpace(req?.Content) ? null : req.Content.Trim();
            var mediaUrl = NormalizeToRelative(req?.MediaUrl);

            if (messageType == "text" && string.IsNullOrWhiteSpace(content))
            {
                return BadRequest(new { message = "Nội dung tin nhắn không được để trống." });
            }

            if (messageType == "image" && string.IsNullOrWhiteSpace(mediaUrl))
            {
                return BadRequest(new { message = "Ảnh tin nhắn không được để trống." });
            }

            if (messageType == "text" && ContainsObjectionableChatContent(content))
            {
                return BadRequest(new { message = "Nội dung tin nhắn vi phạm tiêu chuẩn cộng đồng." });
            }

            if (req?.ClientMessageId != null)
            {
                var existing = await _db.DirectChatMessages
                    .AsNoTracking()
                    .Include(x => x.SenderUser)
                    .Include(x => x.ReplyToMessage)
                    .FirstOrDefaultAsync(x =>
                        x.SenderUserId == userId &&
                        x.ClientMessageId == req.ClientMessageId &&
                        !x.IsDeleted,
                        ct);

                if (existing != null)
                {
                    return Ok(new
                    {
                        message = "Tin nhắn đã được ghi nhận.",
                        item = BuildMessageItem(existing)
                    });
                }
            }

            if (req?.ReplyToMessageId != null)
            {
                var replyExists = await _db.DirectChatMessages
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.DirectChatMessageId == req.ReplyToMessageId &&
                        x.DirectChatRoomId == roomId &&
                        !x.IsDeleted,
                        ct);

                if (!replyExists)
                {
                    return BadRequest(new { message = "Tin nhắn được trả lời không tồn tại." });
                }
            }

            var now = DateTime.UtcNow;
            var entity = new DirectChatMessage
            {
                DirectChatRoomId = roomId,
                SenderUserId = userId,
                MessageType = messageType,
                Content = content,
                MediaUrl = mediaUrl,
                ReplyToMessageId = req?.ReplyToMessageId,
                ClientMessageId = req?.ClientMessageId,
                SentAt = now,
                IsRecalled = false,
                IsDeleted = false
            };

            _db.DirectChatMessages.Add(entity);
            await _db.SaveChangesAsync(ct);

            room.LastMessageId = entity.DirectChatMessageId;
            room.LastMessageAt = entity.SentAt;
            room.UpdatedAt = now;
            await TouchParticipantsAsync(roomId, new[] { userId, otherUserId }, now, ct);
            await _db.SaveChangesAsync(ct);

            var saved = await LoadMessageByIdAsync(entity.DirectChatMessageId, ct);
            var item = BuildMessageItem(saved!);

            await _realtimeHub.SendDirectMessageCreatedAsync(roomId, item);
            await _realtimeHub.SendDirectNotificationToUserAsync(otherUserId.ToString(), new
            {
                kind = "new_direct_message",
                roomId,
                directChatRoomId = roomId,
                senderUserId = userId,
                senderName = saved!.SenderUser.FullName,
                messagePreview = messageType == "text" ? content : "[Hình ảnh]",
                sentAt = saved.SentAt
            });

            return Ok(new
            {
                message = "Gửi tin nhắn thành công.",
                item
            });
        }

        [HttpPost("messages/{messageId:long}/recall")]
        public async Task<IActionResult> RecallMessage(long messageId, CancellationToken ct)
        {
            var userId = GetUserIdFromToken();
            var message = await _db.DirectChatMessages
                .Include(x => x.DirectChatRoom)
                .FirstOrDefaultAsync(x => x.DirectChatMessageId == messageId && !x.IsDeleted, ct);

            if (message == null || !IsRoomMember(message.DirectChatRoom, userId))
            {
                return NotFound(new { message = "Tin nhắn không tồn tại." });
            }

            if (message.SenderUserId != userId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Bạn chỉ có thể thu hồi tin nhắn của chính mình."
                });
            }

            if (!message.IsRecalled)
            {
                var now = DateTime.UtcNow;
                message.IsRecalled = true;
                message.RecalledAt = now;
                message.RecalledByUserId = userId;
                message.Content = null;
                message.MediaUrl = null;
                message.DirectChatRoom.UpdatedAt = now;
                await _db.SaveChangesAsync(ct);
            }

            var saved = await LoadMessageByIdAsync(messageId, ct);
            var item = BuildMessageItem(saved!);

            await _realtimeHub.SendDirectMessageRecalledAsync(message.DirectChatRoomId, messageId, item);

            return Ok(new
            {
                message = "Đã thu hồi tin nhắn.",
                item
            });
        }

        [HttpPost("users/{targetUserId:long}/block")]
        public async Task<IActionResult> BlockUser(
            long targetUserId,
            [FromBody] DirectChatBlockRequestDto? req,
            CancellationToken ct)
        {
            var userId = GetUserIdFromToken();
            if (targetUserId == userId)
            {
                return BadRequest(new { message = "Bạn không thể chặn chính mình." });
            }

            var target = await _db.Users
                .AsNoTracking()
                .Where(x => x.UserId == targetUserId && x.IsActive)
                .Select(x => new { x.UserId, x.FullName })
                .FirstOrDefaultAsync(ct);

            if (target == null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng." });
            }

            var room = await ResolveRoomForBlockAsync(userId, targetUserId, req?.RoomId, ct);
            if (req?.RoomId != null && room == null)
            {
                return NotFound(new { message = "Không tìm thấy cuộc trò chuyện." });
            }

            var sourceMessageId = await ResolveBlockMessageIdAsync(room?.DirectChatRoomId, targetUserId, req?.MessageId, ct);
            var existing = await _db.UserBlocks
                .FirstOrDefaultAsync(x =>
                    x.BlockerUserId == userId &&
                    x.BlockedUserId == targetUserId &&
                    x.IsActive,
                    ct);

            if (existing == null)
            {
                existing = new UserBlock
                {
                    BlockerUserId = userId,
                    BlockedUserId = targetUserId,
                    SourceDirectRoomId = room?.DirectChatRoomId,
                    SourceDirectMessageId = sourceMessageId,
                    ReasonCode = NormalizeReasonCode(req?.Reason),
                    Notes = Clean(req?.Notes, 500),
                    Source = "DIRECT_CHAT",
                    IsActive = true,
                    BlockedAt = DateTime.UtcNow
                };

                _db.UserBlocks.Add(existing);
            }
            else
            {
                existing.SourceDirectRoomId ??= room?.DirectChatRoomId;
                existing.SourceDirectMessageId ??= sourceMessageId;
                existing.Notes = Clean(req?.Notes, 500) ?? existing.Notes;
            }

            await _db.SaveChangesAsync(ct);

            var payload = new
            {
                blockerUserId = userId,
                blockedUserId = targetUserId,
                roomId = room?.DirectChatRoomId,
                directChatRoomId = room?.DirectChatRoomId,
                isBlocked = true
            };

            await _realtimeHub.SendDirectBlockChangedAsync(userId.ToString(), payload);
            await _realtimeHub.SendDirectBlockChangedAsync(targetUserId.ToString(), payload);

            return Ok(new
            {
                message = "Đã chặn người dùng.",
                item = new
                {
                    blockId = existing.BlockId,
                    userId = target.UserId,
                    fullName = target.FullName,
                    roomId = room?.DirectChatRoomId,
                    directChatRoomId = room?.DirectChatRoomId,
                    messageId = sourceMessageId,
                    reason = ToApiReasonCode(existing.ReasonCode),
                    notes = existing.Notes,
                    source = "direct_chat",
                    blockedAt = existing.BlockedAt,
                    isBlockedByMe = true
                }
            });
        }

        [HttpDelete("users/{targetUserId:long}/block")]
        public async Task<IActionResult> UnblockUser(long targetUserId, CancellationToken ct)
        {
            var userId = GetUserIdFromToken();
            var entity = await _db.UserBlocks
                .FirstOrDefaultAsync(x =>
                    x.BlockerUserId == userId &&
                    x.BlockedUserId == targetUserId &&
                    x.IsActive,
                    ct);

            var removed = false;
            long? roomId = null;

            if (entity != null)
            {
                removed = true;
                roomId = entity.SourceDirectRoomId;
                entity.IsActive = false;
                entity.UnblockedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            var payload = new
            {
                blockerUserId = userId,
                blockedUserId = targetUserId,
                roomId,
                directChatRoomId = roomId,
                isBlocked = false
            };

            await _realtimeHub.SendDirectBlockChangedAsync(userId.ToString(), payload);
            await _realtimeHub.SendDirectBlockChangedAsync(targetUserId.ToString(), payload);

            return Ok(new
            {
                message = removed ? "Đã bỏ chặn người dùng." : "Người dùng này chưa bị chặn.",
                blockedUserId = targetUserId,
                removed
            });
        }

        private long GetUserIdFromToken()
        {
            var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid) || !long.TryParse(uid, out var userId))
            {
                throw new UnauthorizedAccessException("Invalid token: missing uid.");
            }

            return userId;
        }

        private async Task<DirectChatRoom?> LoadRoomWithUsersAsync(long user1Id, long user2Id, CancellationToken ct)
        {
            return await _db.DirectChatRooms
                .Include(x => x.User1)
                .Include(x => x.User2)
                .Include(x => x.LastMessage)
                    .ThenInclude(x => x!.SenderUser)
                .FirstOrDefaultAsync(x => x.User1Id == user1Id && x.User2Id == user2Id, ct);
        }

        private async Task<DirectChatRoom?> LoadRoomByIdWithUsersAsync(long roomId, CancellationToken ct)
        {
            return await _db.DirectChatRooms
                .Include(x => x.User1)
                .Include(x => x.User2)
                .Include(x => x.LastMessage)
                    .ThenInclude(x => x!.SenderUser)
                .FirstOrDefaultAsync(x => x.DirectChatRoomId == roomId && x.IsActive, ct);
        }

        private async Task<DirectChatMessage?> LoadMessageByIdAsync(long messageId, CancellationToken ct)
        {
            return await _db.DirectChatMessages
                .AsNoTracking()
                .Include(x => x.SenderUser)
                .Include(x => x.ReplyToMessage)
                .FirstOrDefaultAsync(x => x.DirectChatMessageId == messageId, ct);
        }

        private async Task MarkRoomReadAsync(long roomId, long userId, long messageId, CancellationToken ct)
        {
            var participant = await _db.DirectChatRoomParticipants
                .FirstOrDefaultAsync(x => x.DirectChatRoomId == roomId && x.UserId == userId, ct);

            if (participant == null)
            {
                participant = new DirectChatRoomParticipant
                {
                    DirectChatRoomId = roomId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.DirectChatRoomParticipants.Add(participant);
            }

            participant.LastReadMessageId = messageId;
            participant.LastReadAt = DateTime.UtcNow;
            participant.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        private async Task TouchParticipantsAsync(long roomId, IEnumerable<long> userIds, DateTime now, CancellationToken ct)
        {
            var existing = await _db.DirectChatRoomParticipants
                .Where(x => x.DirectChatRoomId == roomId && userIds.Contains(x.UserId))
                .ToListAsync(ct);

            foreach (var userId in userIds.Distinct())
            {
                var participant = existing.FirstOrDefault(x => x.UserId == userId);
                if (participant == null)
                {
                    _db.DirectChatRoomParticipants.Add(new DirectChatRoomParticipant
                    {
                        DirectChatRoomId = roomId,
                        UserId = userId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    continue;
                }

                participant.IsDeleted = false;
                participant.DeletedAt = null;
                participant.IsArchived = false;
                participant.ArchivedAt = null;
                participant.UpdatedAt = now;
            }
        }

        private async Task<DirectChatRoom?> ResolveRoomForBlockAsync(
            long userId,
            long targetUserId,
            long? requestedRoomId,
            CancellationToken ct)
        {
            if (requestedRoomId.HasValue && requestedRoomId.Value > 0)
            {
                return await _db.DirectChatRooms
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.DirectChatRoomId == requestedRoomId.Value &&
                        x.IsActive &&
                        ((x.User1Id == userId && x.User2Id == targetUserId) ||
                         (x.User1Id == targetUserId && x.User2Id == userId)),
                        ct);
            }

            var pair = NormalizePair(userId, targetUserId);
            return await _db.DirectChatRooms
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.User1Id == pair.User1Id && x.User2Id == pair.User2Id && x.IsActive, ct);
        }

        private async Task<long?> ResolveBlockMessageIdAsync(
            long? roomId,
            long blockedUserId,
            long? requestedMessageId,
            CancellationToken ct)
        {
            if (!roomId.HasValue || !requestedMessageId.HasValue || requestedMessageId.Value <= 0)
            {
                return null;
            }

            var exists = await _db.DirectChatMessages
                .AsNoTracking()
                .AnyAsync(x =>
                    x.DirectChatMessageId == requestedMessageId.Value &&
                    x.DirectChatRoomId == roomId.Value &&
                    x.SenderUserId == blockedUserId &&
                    !x.IsDeleted,
                    ct);

            return exists ? requestedMessageId.Value : null;
        }

        private async Task<bool> HasActiveBlockEitherDirectionAsync(long userId, long targetUserId, CancellationToken ct)
        {
            return await _db.UserBlocks
                .AsNoTracking()
                .AnyAsync(x =>
                    x.IsActive &&
                    ((x.BlockerUserId == userId && x.BlockedUserId == targetUserId) ||
                     (x.BlockerUserId == targetUserId && x.BlockedUserId == userId)),
                    ct);
        }

        private async Task<List<UserBlock>> LoadBlockStatesAsync(
            long userId,
            IEnumerable<long> targetUserIds,
            CancellationToken ct)
        {
            var ids = targetUserIds
                .Where(x => x > 0 && x != userId)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return new List<UserBlock>();
            }

            return await _db.UserBlocks
                .AsNoTracking()
                .Where(x =>
                    x.IsActive &&
                    ((x.BlockerUserId == userId && ids.Contains(x.BlockedUserId)) ||
                     (x.BlockedUserId == userId && ids.Contains(x.BlockerUserId))))
                .ToListAsync(ct);
        }

        private static (bool IsBlockedByMe, bool HasBlockedMe) GetBlockState(
            IEnumerable<UserBlock> blocks,
            long userId,
            long targetUserId)
        {
            var isBlockedByMe = blocks.Any(x =>
                x.BlockerUserId == userId &&
                x.BlockedUserId == targetUserId &&
                x.IsActive);

            var hasBlockedMe = blocks.Any(x =>
                x.BlockerUserId == targetUserId &&
                x.BlockedUserId == userId &&
                x.IsActive);

            return (isBlockedByMe, hasBlockedMe);
        }

        private object BuildRoomItem(
            DirectChatRoom room,
            long currentUserId,
            IEnumerable<UserBlock> blocks,
            IReadOnlyDictionary<long, int> unreadMap)
        {
            var other = room.User1Id == currentUserId ? room.User2 : room.User1;
            var otherUserId = other.UserId;
            var state = GetBlockState(blocks, currentUserId, otherUserId);
            var lastMessage = room.LastMessage;

            string? preview = null;
            if (lastMessage != null)
            {
                if (lastMessage.IsRecalled)
                {
                    preview = "Tin nhắn đã được thu hồi.";
                }
                else if (state.IsBlockedByMe && lastMessage.SenderUserId == otherUserId)
                {
                    preview = "Tin nhắn từ người dùng bị chặn đã được ẩn.";
                }
                else
                {
                    preview = lastMessage.MessageType == "text"
                        ? lastMessage.Content
                        : "[Hình ảnh]";
                }
            }

            return new
            {
                roomId = room.DirectChatRoomId,
                directChatRoomId = room.DirectChatRoomId,
                title = other.FullName,
                avatarUrl = ToAbsoluteUrl(other.AvatarUrl),
                otherUser = new
                {
                    userId = other.UserId,
                    fullName = other.FullName,
                    phone = MaskPhone(other.Phone),
                    city = other.City,
                    gender = other.Gender,
                    verified = other.Verified,
                    avatarUrl = ToAbsoluteUrl(other.AvatarUrl)
                },
                lastMessagePreview = preview,
                lastMessageType = lastMessage?.MessageType,
                lastMessageAt = room.LastMessageAt,
                lastSenderUserId = lastMessage?.SenderUserId,
                lastSenderName = lastMessage?.SenderUser.FullName,
                unreadCount = unreadMap.TryGetValue(room.DirectChatRoomId, out var unreadCount)
                    ? unreadCount
                    : 0,
                isBlockedByMe = state.IsBlockedByMe,
                hasBlockedMe = state.HasBlockedMe,
                canSend = !state.IsBlockedByMe && !state.HasBlockedMe,
                updatedAt = room.UpdatedAt
            };
        }

        private object BuildMessageItem(DirectChatMessage message)
        {
            var isRecalled = message.IsRecalled;

            return new
            {
                messageId = message.DirectChatMessageId,
                directChatMessageId = message.DirectChatMessageId,
                roomId = message.DirectChatRoomId,
                directChatRoomId = message.DirectChatRoomId,
                senderUserId = message.SenderUserId,
                messageType = message.MessageType,
                content = isRecalled ? null : message.Content,
                mediaUrl = isRecalled ? null : ToAbsoluteUrl(message.MediaUrl),
                replyToMessageId = message.ReplyToMessageId,
                sentAt = message.SentAt,
                isRecalled,
                recalledAt = message.RecalledAt,
                recalledByUserId = message.RecalledByUserId,
                sender = new
                {
                    userId = message.SenderUser.UserId,
                    fullName = message.SenderUser.FullName,
                    avatarUrl = ToAbsoluteUrl(message.SenderUser.AvatarUrl)
                },
                replyTo = message.ReplyToMessage == null ? null : new
                {
                    messageId = message.ReplyToMessage.DirectChatMessageId,
                    content = message.ReplyToMessage.IsRecalled ? null : message.ReplyToMessage.Content,
                    messageType = message.ReplyToMessage.MessageType,
                    senderUserId = message.ReplyToMessage.SenderUserId,
                    isRecalled = message.ReplyToMessage.IsRecalled
                }
            };
        }

        private static (long User1Id, long User2Id) NormalizePair(long userId, long targetUserId)
        {
            return userId < targetUserId
                ? (userId, targetUserId)
                : (targetUserId, userId);
        }

        private static bool IsRoomMember(DirectChatRoom room, long userId)
        {
            return room.User1Id == userId || room.User2Id == userId;
        }

        private static long GetOtherUserId(DirectChatRoom room, long userId)
        {
            return room.User1Id == userId ? room.User2Id : room.User1Id;
        }

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var trimmed = url.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (!trimmed.StartsWith("/"))
            {
                trimmed = "/" + trimmed;
            }

            if (Request?.Host.HasValue == true)
            {
                var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value : string.Empty;
                return $"{Request.Scheme}://{Request.Host}{pathBase}{trimmed}";
            }

            var baseUrl = _config["PublicBaseUrl"]?.TrimEnd('/');
            return string.IsNullOrWhiteSpace(baseUrl) ? trimmed : $"{baseUrl}{trimmed}";
        }

        private static string? NormalizeToRelative(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            url = url.Trim();
            if (url.StartsWith("/")) return url;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.PathAndQuery;
            }

            return url;
        }

        private static string NormalizeMessageType(string? messageType)
        {
            var normalized = (messageType ?? "text").Trim().ToLowerInvariant();
            return normalized switch
            {
                "text" => "text",
                "image" => "image",
                "photo" => "image",
                _ => normalized
            };
        }

        private static bool ContainsObjectionableChatContent(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;

            return UnsafeChatTerms.Any(term =>
                content.Contains(term, StringComparison.OrdinalIgnoreCase));
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

        private static string ToApiReasonCode(string? reasonCode)
        {
            return (reasonCode ?? "OTHER").Trim().ToLowerInvariant();
        }

        private static string? Clean(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength
                ? trimmed
                : trimmed[..maxLength];
        }

        private static string? MaskPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;

            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length <= 4) return phone;

            return $"{digits[..Math.Min(3, digits.Length)]}***{digits[^4..]}";
        }
    }
}
