using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HanakaServer.Services
{
    public class RealtimeHub
    {
        // userId -> many sockets
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>> _userSockets = new();

        // socketId -> subscribed clubIds
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte>> _socketClubSubscriptions = new();

        // socketId -> subscribed direct chat roomIds
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte>> _socketDirectRoomSubscriptions = new();

        // socketId -> userId
        private readonly ConcurrentDictionary<string, string> _socketToUser = new();

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public string AddSocket(string userId, WebSocket socket)
        {
            var socketId = Guid.NewGuid().ToString("N");

            var sockets = _userSockets.GetOrAdd(userId, _ => new ConcurrentDictionary<string, WebSocket>());
            sockets[socketId] = socket;

            _socketClubSubscriptions.TryAdd(socketId, new ConcurrentDictionary<long, byte>());
            _socketDirectRoomSubscriptions.TryAdd(socketId, new ConcurrentDictionary<long, byte>());
            _socketToUser[socketId] = userId;

            return socketId;
        }

        public async Task RemoveSocketAsync(string socketId)
        {
            if (_socketToUser.TryRemove(socketId, out var userId))
            {
                if (_userSockets.TryGetValue(userId, out var sockets))
                {
                    sockets.TryRemove(socketId, out _);
                    if (sockets.IsEmpty)
                        _userSockets.TryRemove(userId, out _);
                }
            }

            _socketClubSubscriptions.TryRemove(socketId, out _);
            _socketDirectRoomSubscriptions.TryRemove(socketId, out _);
            await Task.CompletedTask;
        }

        public void SubscribeClub(string socketId, long clubId)
        {
            if (_socketClubSubscriptions.TryGetValue(socketId, out var set))
                set[clubId] = 1;
        }

        public void UnsubscribeClub(string socketId, long clubId)
        {
            if (_socketClubSubscriptions.TryGetValue(socketId, out var set))
                set.TryRemove(clubId, out _);
        }

        public bool IsSocketSubscribedToClub(string socketId, long clubId)
        {
            return _socketClubSubscriptions.TryGetValue(socketId, out var set) && set.ContainsKey(clubId);
        }

        public void SubscribeDirectRoom(string socketId, long roomId)
        {
            if (_socketDirectRoomSubscriptions.TryGetValue(socketId, out var set))
                set[roomId] = 1;
        }

        public void UnsubscribeDirectRoom(string socketId, long roomId)
        {
            if (_socketDirectRoomSubscriptions.TryGetValue(socketId, out var set))
                set.TryRemove(roomId, out _);
        }

        public bool IsSocketSubscribedToDirectRoom(string socketId, long roomId)
        {
            return _socketDirectRoomSubscriptions.TryGetValue(socketId, out var set) && set.ContainsKey(roomId);
        }

        public IEnumerable<string> GetSocketIdsOfUser(string userId)
        {
            if (!_userSockets.TryGetValue(userId, out var sockets))
                return Enumerable.Empty<string>();

            return sockets.Keys.ToList();
        }

        public async Task SendToUserAsync(string userId, object message)
        {
            if (!_userSockets.TryGetValue(userId, out var sockets) || sockets.IsEmpty)
                return;

            var bytes = Serialize(message);

            foreach (var kv in sockets)
            {
                await SafeSendAsync(kv.Value, bytes);
            }
        }

        public async Task BroadcastToClubAsync(long clubId, object message, string? excludeUserId = null)
        {
            var bytes = Serialize(message);

            foreach (var pair in _socketClubSubscriptions)
            {
                var socketId = pair.Key;
                var clubSet = pair.Value;

                if (!clubSet.ContainsKey(clubId))
                    continue;

                if (!string.IsNullOrWhiteSpace(excludeUserId)
                    && _socketToUser.TryGetValue(socketId, out var uid)
                    && uid == excludeUserId)
                {
                    continue;
                }

                if (_socketToUser.TryGetValue(socketId, out var userId)
                    && _userSockets.TryGetValue(userId, out var sockets)
                    && sockets.TryGetValue(socketId, out var ws))
                {
                    await SafeSendAsync(ws, bytes);
                }
            }
        }

        public async Task BroadcastToDirectRoomAsync(long roomId, object message, string? excludeUserId = null)
        {
            var bytes = Serialize(message);

            foreach (var pair in _socketDirectRoomSubscriptions)
            {
                var socketId = pair.Key;
                var roomSet = pair.Value;

                if (!roomSet.ContainsKey(roomId))
                    continue;

                if (!string.IsNullOrWhiteSpace(excludeUserId)
                    && _socketToUser.TryGetValue(socketId, out var uid)
                    && uid == excludeUserId)
                {
                    continue;
                }

                if (_socketToUser.TryGetValue(socketId, out var userId)
                    && _userSockets.TryGetValue(userId, out var sockets)
                    && sockets.TryGetValue(socketId, out var ws))
                {
                    await SafeSendAsync(ws, bytes);
                }
            }
        }

        public Task SendClubMessageCreatedAsync(long clubId, object item, string? excludeUserId = null)
        {
            return BroadcastToClubAsync(clubId, new
            {
                type = "club.message.created",
                clubId,
                item
            }, excludeUserId);
        }

        public Task SendClubMessageDeletedAsync(long clubId, long messageId, string? excludeUserId = null)
        {
            return BroadcastToClubAsync(clubId, new
            {
                type = "club.message.deleted",
                clubId,
                messageId
            }, excludeUserId);
        }

        public Task SendTypingToClubAsync(long clubId, string userId, string fullName, bool isTyping)
        {
            return BroadcastToClubAsync(clubId, new
            {
                type = "club.typing",
                clubId,
                userId,
                fullName,
                isTyping
            }, excludeUserId: userId);
        }

        public Task SendDirectMessageCreatedAsync(long roomId, object item, string? excludeUserId = null)
        {
            return BroadcastToDirectRoomAsync(roomId, new
            {
                type = "direct.message.created",
                roomId,
                directChatRoomId = roomId,
                item
            }, excludeUserId);
        }

        public Task SendDirectMessageRecalledAsync(long roomId, long messageId, object item, string? excludeUserId = null)
        {
            return BroadcastToDirectRoomAsync(roomId, new
            {
                type = "direct.message.recalled",
                roomId,
                directChatRoomId = roomId,
                messageId,
                item
            }, excludeUserId);
        }

        public Task SendTypingToDirectRoomAsync(long roomId, string userId, string fullName, bool isTyping)
        {
            return BroadcastToDirectRoomAsync(roomId, new
            {
                type = "direct.typing",
                roomId,
                directChatRoomId = roomId,
                userId,
                fullName,
                isTyping
            }, excludeUserId: userId);
        }

        public Task SendDirectNotificationToUserAsync(string userId, object payload)
        {
            return SendToUserAsync(userId, new
            {
                type = "direct.notification",
                payload
            });
        }

        public Task SendDirectBlockChangedAsync(string userId, object payload)
        {
            return SendToUserAsync(userId, new
            {
                type = "direct.block.changed",
                payload
            });
        }

        public Task SendNotificationToUserAsync(string userId, object payload)
        {
            return SendToUserAsync(userId, new
            {
                type = "club.notification",
                payload
            });
        }

        public Task SendTournamentNotificationToUserAsync(string userId, object payload)
        {
            return SendToUserAsync(userId, new
            {
                type = "tournament.notification",
                payload
            });
        }

        public async Task DisconnectUserAsync(string userId, string reason = "session_revoked")
        {
            if (!_userSockets.TryGetValue(userId, out var sockets) || sockets.IsEmpty)
                return;

            var socketPairs = sockets.ToArray();
            var bytes = Serialize(new
            {
                type = "session.revoked",
                reason
            });

            foreach (var pair in socketPairs)
            {
                var socketId = pair.Key;
                var ws = pair.Value;

                await SafeSendAsync(ws, bytes);

                try
                {
                    if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    {
                        await ws.CloseAsync(
                            WebSocketCloseStatus.PolicyViolation,
                            reason,
                            CancellationToken.None);
                    }
                }
                catch
                {
                }

                await RemoveSocketAsync(socketId);
            }
        }

        private static byte[] Serialize(object message)
        {
            var json = JsonSerializer.Serialize(message, JsonOpts);
            return Encoding.UTF8.GetBytes(json);
        }

        private static async Task SafeSendAsync(WebSocket ws, byte[] bytes)
        {
            try
            {
                if (ws.State != WebSocketState.Open) return;
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch
            {
            }
        }
    }
}
