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

        // socketId -> userId
        private readonly ConcurrentDictionary<string, string> _socketToUser = new();

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public string AddSocket(string userId, WebSocket socket)
        {
            var socketId = Guid.NewGuid().ToString("N");

            var sockets = _userSockets.GetOrAdd(userId, _ => new ConcurrentDictionary<string, WebSocket>());
            sockets[socketId] = socket;

            _socketClubSubscriptions.TryAdd(socketId, new ConcurrentDictionary<long, byte>());
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

        public Task SendNotificationToUserAsync(string userId, object payload)
        {
            return SendToUserAsync(userId, new
            {
                type = "club.notification",
                payload
            });
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