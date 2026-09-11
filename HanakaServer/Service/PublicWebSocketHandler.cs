using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace HanakaServer.Services
{
    public class PublicWebSocketHandler
    {
        private readonly PublicRealtimeHub _hub;
        private readonly ILogger<PublicWebSocketHandler> _logger;

        public PublicWebSocketHandler(
            PublicRealtimeHub hub,
            ILogger<PublicWebSocketHandler>? logger = null)
        {
            _hub = hub;
            _logger = logger ?? NullLogger<PublicWebSocketHandler>.Instance;
        }

        public async Task HandleAsync(WebSocket ws, CancellationToken ct)
        {
            var socketId = _hub.AddSocket(ws);
            await _hub.SendToSocketAsync(socketId, new { type = "hello.public" });

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

                    await HandleClientMessageAsync(socketId, message);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (WebSocketException ex)
            {
                _logger.LogDebug(ex, "Public WebSocket {SocketId} disconnected.", socketId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Public WebSocket {SocketId} receive loop failed.", socketId);
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
                catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
                {
                    _logger.LogDebug(ex, "Public WebSocket {SocketId} was already closed during cleanup.", socketId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Public WebSocket {SocketId} cleanup failed.", socketId);
                }

                await _hub.RemoveSocketAsync(socketId);
            }
        }

        private async Task HandleClientMessageAsync(string socketId, string json)
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
                    await _hub.SendToSocketAsync(socketId, new { type = "pong" });
                    break;

                case "tournament.subscribe":
                    if (TryGetPositiveInt64(doc.RootElement, "tournamentId", out var tournamentId))
                    {
                        _hub.SubscribeTournament(socketId, tournamentId);
                        await _hub.SendToSocketAsync(socketId, new { type = "tournament.subscribed", tournamentId });
                    }
                    break;

                case "tournament.unsubscribe":
                    if (TryGetPositiveInt64(doc.RootElement, "tournamentId", out var unsubscribeTournamentId))
                    {
                        _hub.UnsubscribeTournament(socketId, unsubscribeTournamentId);
                        await _hub.SendToSocketAsync(socketId, new { type = "tournament.unsubscribed", tournamentId = unsubscribeTournamentId });
                    }
                    break;

                case "match.subscribe":
                    if (TryGetPositiveInt64(doc.RootElement, "matchId", out var matchId))
                    {
                        _hub.SubscribeMatch(socketId, matchId);
                        await _hub.SendToSocketAsync(socketId, new { type = "match.subscribed", matchId });
                    }
                    break;

                case "match.unsubscribe":
                    if (TryGetPositiveInt64(doc.RootElement, "matchId", out var unsubscribeMatchId))
                    {
                        _hub.UnsubscribeMatch(socketId, unsubscribeMatchId);
                        await _hub.SendToSocketAsync(socketId, new { type = "match.unsubscribed", matchId = unsubscribeMatchId });
                    }
                    break;

                case "videos.subscribe":
                    _hub.SubscribeVideosFeed(socketId);
                    await _hub.SendToSocketAsync(socketId, new { type = "videos.subscribed" });
                    break;

                case "videos.unsubscribe":
                    _hub.UnsubscribeVideosFeed(socketId);
                    await _hub.SendToSocketAsync(socketId, new { type = "videos.unsubscribed" });
                    break;

                case "payment.subscribe":
                    if (TryGetNonEmptyString(doc.RootElement, "transactionCode", out var transactionCode))
                    {
                        _hub.SubscribePayment(socketId, transactionCode);
                        await _hub.SendToSocketAsync(socketId, new { type = "payment.subscribed", transactionCode });
                    }
                    break;

                case "payment.unsubscribe":
                    if (TryGetNonEmptyString(doc.RootElement, "transactionCode", out var unsubscribeTransactionCode))
                    {
                        _hub.UnsubscribePayment(socketId, unsubscribeTransactionCode);
                        await _hub.SendToSocketAsync(socketId, new { type = "payment.unsubscribed", transactionCode = unsubscribeTransactionCode });
                    }
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

        private static bool TryGetNonEmptyString(JsonElement root, string propertyName, out string value)
        {
            value = string.Empty;
            if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = property.GetString()?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
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

    }
}
