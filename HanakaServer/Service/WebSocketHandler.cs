using HanakaServer.Data;
using Microsoft.EntityFrameworkCore;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace HanakaServer.Services
{
    public class WebSocketHandler
    {
        private readonly RealtimeHub _hub;
        private readonly IServiceScopeFactory _scopeFactory;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public WebSocketHandler(
            RealtimeHub hub,
            IServiceScopeFactory scopeFactory)
        {
            _hub = hub;
            _scopeFactory = scopeFactory;
        }

        public async Task HandleAsync(WebSocket ws, string userId, CancellationToken ct)
        {
            var socketId = _hub.AddSocket(userId, ws);
            await SendAsync(ws, new { type = "hello", userId }, ct);

            var buffer = new byte[8 * 1024];

            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(buffer, ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var msg = await ReadFullMessageAsync(ws, buffer, result, ct);
                    if (string.IsNullOrWhiteSpace(msg)) continue;

                    await HandleClientMessageAsync(socketId, userId, ws, msg, ct);
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch { }

                await _hub.RemoveSocketAsync(socketId);
            }
        }

        private async Task HandleClientMessageAsync(string socketId, string userId, WebSocket ws, string json, CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("type", out var typeEl))
                return;

            var type = typeEl.GetString()?.Trim().ToLowerInvariant();

            switch (type)
            {
                case "ping":
                    await SendAsync(ws, new { type = "pong" }, ct);
                    break;

                case "club.subscribe":
                    if (doc.RootElement.TryGetProperty("clubId", out var clubEl)
                        && clubEl.TryGetInt64(out var clubId)
                        && clubId > 0)
                    {
                        var canJoin = await CanAccessClubChatAsync(clubId, userId);
                        if (!canJoin)
                        {
                            await SendAsync(ws, new
                            {
                                type = "club.error",
                                clubId,
                                message = "Bạn không phải thành viên CLB."
                            }, ct);
                            return;
                        }

                        _hub.SubscribeClub(socketId, clubId);

                        await SendAsync(ws, new
                        {
                            type = "club.subscribed",
                            clubId
                        }, ct);
                    }
                    break;

                case "club.unsubscribe":
                    if (doc.RootElement.TryGetProperty("clubId", out var clubEl2)
                        && clubEl2.TryGetInt64(out var clubId2)
                        && clubId2 > 0)
                    {
                        _hub.UnsubscribeClub(socketId, clubId2);

                        await SendAsync(ws, new
                        {
                            type = "club.unsubscribed",
                            clubId = clubId2
                        }, ct);
                    }
                    break;

                case "club.typing":
                    if (doc.RootElement.TryGetProperty("clubId", out var clubEl3)
                        && clubEl3.TryGetInt64(out var clubId3)
                        && clubId3 > 0)
                    {
                        var canJoin = await CanAccessClubChatAsync(clubId3, userId);
                        if (!canJoin) return;

                        var isTyping = false;
                        if (doc.RootElement.TryGetProperty("isTyping", out var typingEl)
                            && (typingEl.ValueKind == JsonValueKind.True || typingEl.ValueKind == JsonValueKind.False))
                        {
                            isTyping = typingEl.GetBoolean();
                        }

                        var fullName = await GetUserFullNameAsync(userId);
                        await _hub.SendTypingToClubAsync(clubId3, userId, fullName, isTyping);
                    }
                    break;
            }
        }

        private async Task<bool> CanAccessClubChatAsync(long clubId, string userId)
        {
            if (!long.TryParse(userId, out var uid))
                return false;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PickleballDbContext>();

            return await db.ClubMembers.AnyAsync(x =>
                x.ClubId == clubId &&
                x.UserId == uid &&
                x.IsActive &&
                x.Club.IsActive);
        }

        private async Task<string> GetUserFullNameAsync(string userId)
        {
            if (!long.TryParse(userId, out var uid))
                return "Thành viên";

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PickleballDbContext>();

            var fullName = await db.Users
                .Where(x => x.UserId == uid)
                .Select(x => x.FullName)
                .FirstOrDefaultAsync();

            return string.IsNullOrWhiteSpace(fullName) ? "Thành viên" : fullName;
        }

        private static async Task<string> ReadFullMessageAsync(WebSocket ws, byte[] buffer, WebSocketReceiveResult first, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.Append(Encoding.UTF8.GetString(buffer, 0, first.Count));

            while (!first.EndOfMessage)
            {
                var r = await ws.ReceiveAsync(buffer, ct);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                first = r;
            }

            return sb.ToString();
        }

        private static Task SendAsync(WebSocket ws, object obj, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj, JsonOpts));
            return ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
    }
}