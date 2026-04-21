using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HanakaServer.Services
{
    public class PublicWebSocketHandler
    {
        private readonly PublicRealtimeHub _hub;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public PublicWebSocketHandler(PublicRealtimeHub hub)
        {
            _hub = hub;
        }

        public async Task HandleAsync(WebSocket ws, CancellationToken ct)
        {
            var socketId = _hub.AddSocket(ws);
            await SendAsync(ws, new { type = "hello.public" }, ct);

            var buffer = new byte[8 * 1024];

            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(buffer, ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    var message = await ReadFullMessageAsync(ws, buffer, result, ct);
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        continue;
                    }

                    await HandleClientMessageAsync(socketId, ws, message, ct);
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
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    }
                }
                catch
                {
                }

                await _hub.RemoveSocketAsync(socketId);
            }
        }

        private async Task HandleClientMessageAsync(string socketId, WebSocket ws, string json, CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString()?.Trim().ToLowerInvariant();

            switch (type)
            {
                case "ping":
                    await SendAsync(ws, new { type = "pong" }, ct);
                    break;

                case "tournament.subscribe":
                    if (TryGetPositiveInt64(doc.RootElement, "tournamentId", out var tournamentId))
                    {
                        _hub.SubscribeTournament(socketId, tournamentId);
                        await SendAsync(ws, new { type = "tournament.subscribed", tournamentId }, ct);
                    }
                    break;

                case "tournament.unsubscribe":
                    if (TryGetPositiveInt64(doc.RootElement, "tournamentId", out var unsubscribeTournamentId))
                    {
                        _hub.UnsubscribeTournament(socketId, unsubscribeTournamentId);
                        await SendAsync(ws, new { type = "tournament.unsubscribed", tournamentId = unsubscribeTournamentId }, ct);
                    }
                    break;

                case "match.subscribe":
                    if (TryGetPositiveInt64(doc.RootElement, "matchId", out var matchId))
                    {
                        _hub.SubscribeMatch(socketId, matchId);
                        await SendAsync(ws, new { type = "match.subscribed", matchId }, ct);
                    }
                    break;

                case "match.unsubscribe":
                    if (TryGetPositiveInt64(doc.RootElement, "matchId", out var unsubscribeMatchId))
                    {
                        _hub.UnsubscribeMatch(socketId, unsubscribeMatchId);
                        await SendAsync(ws, new { type = "match.unsubscribed", matchId = unsubscribeMatchId }, ct);
                    }
                    break;

                case "videos.subscribe":
                    _hub.SubscribeVideosFeed(socketId);
                    await SendAsync(ws, new { type = "videos.subscribed" }, ct);
                    break;

                case "videos.unsubscribe":
                    _hub.UnsubscribeVideosFeed(socketId);
                    await SendAsync(ws, new { type = "videos.unsubscribed" }, ct);
                    break;
            }
        }

        private static bool TryGetPositiveInt64(JsonElement root, string propertyName, out long value)
        {
            value = 0;
            return root.TryGetProperty(propertyName, out var property)
                && property.TryGetInt64(out value)
                && value > 0;
        }

        private static async Task<string> ReadFullMessageAsync(WebSocket ws, byte[] buffer, WebSocketReceiveResult first, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.Append(Encoding.UTF8.GetString(buffer, 0, first.Count));

            while (!first.EndOfMessage)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                first = result;
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
