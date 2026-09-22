using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HanakaServer.Services
{
    public class PublicRealtimeHub
    {
        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(3);

        private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _socketSendLocks = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte>> _socketTournamentSubscriptions = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte>> _socketMatchSubscriptions = new();
        private readonly ConcurrentDictionary<string, byte> _socketVideoFeedSubscriptions = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _socketPaymentSubscriptions = new();
        private readonly ILogger<PublicRealtimeHub> _logger;

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public PublicRealtimeHub(ILogger<PublicRealtimeHub> logger)
        {
            _logger = logger;
        }

        public string AddSocket(WebSocket socket)
        {
            var socketId = Guid.NewGuid().ToString("N");
            _sockets[socketId] = socket;
            _socketSendLocks[socketId] = new SemaphoreSlim(1, 1);
            _socketTournamentSubscriptions[socketId] = new ConcurrentDictionary<long, byte>();
            _socketMatchSubscriptions[socketId] = new ConcurrentDictionary<long, byte>();
            _socketPaymentSubscriptions[socketId] = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            return socketId;
        }

        public Task SendToSocketAsync(string socketId, object payload)
        {
            return SafeSendAsync(socketId, Serialize(payload), "direct", "socket.response");
        }

        public async Task RemoveSocketAsync(string socketId)
        {
            _sockets.TryRemove(socketId, out _);
            _socketSendLocks.TryRemove(socketId, out _);
            _socketTournamentSubscriptions.TryRemove(socketId, out _);
            _socketMatchSubscriptions.TryRemove(socketId, out _);
            _socketVideoFeedSubscriptions.TryRemove(socketId, out _);
            _socketPaymentSubscriptions.TryRemove(socketId, out _);
            await Task.CompletedTask;
        }

        public void SubscribeTournament(string socketId, long tournamentId)
        {
            if (_socketTournamentSubscriptions.TryGetValue(socketId, out var subscriptions))
            {
                subscriptions[tournamentId] = 1;
            }
        }

        public void UnsubscribeTournament(string socketId, long tournamentId)
        {
            if (_socketTournamentSubscriptions.TryGetValue(socketId, out var subscriptions))
            {
                subscriptions.TryRemove(tournamentId, out _);
            }
        }

        public void SubscribeMatch(string socketId, long matchId)
        {
            if (_socketMatchSubscriptions.TryGetValue(socketId, out var subscriptions))
            {
                subscriptions[matchId] = 1;
            }
        }

        public void UnsubscribeMatch(string socketId, long matchId)
        {
            if (_socketMatchSubscriptions.TryGetValue(socketId, out var subscriptions))
            {
                subscriptions.TryRemove(matchId, out _);
            }
        }

        public void SubscribeVideosFeed(string socketId)
        {
            _socketVideoFeedSubscriptions[socketId] = 1;
        }

        public void UnsubscribeVideosFeed(string socketId)
        {
            _socketVideoFeedSubscriptions.TryRemove(socketId, out _);
        }

        public void SubscribePayment(string socketId, string transactionCode)
        {
            var normalizedCode = NormalizeTransactionCode(transactionCode);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return;
            }

            if (_socketPaymentSubscriptions.TryGetValue(socketId, out var subscriptions))
            {
                subscriptions[normalizedCode] = 1;
            }
        }

        public void UnsubscribePayment(string socketId, string transactionCode)
        {
            var normalizedCode = NormalizeTransactionCode(transactionCode);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return;
            }

            if (_socketPaymentSubscriptions.TryGetValue(socketId, out var subscriptions))
            {
                subscriptions.TryRemove(normalizedCode, out _);
            }
        }

        public Task BroadcastMatchScoreUpdatedAsync(long tournamentId, long matchId, object payload) =>
            BroadcastMatchUpdatedAsync(tournamentId, matchId, payload, "tournament.match.score.updated");

        public Task BroadcastMatchCoordinationUpdatedAsync(long tournamentId, long matchId, object payload) =>
            BroadcastMatchUpdatedAsync(tournamentId, matchId, payload, "tournament.match.coordination.updated");

        private async Task BroadcastMatchUpdatedAsync(long tournamentId, long matchId, object payload, string eventType)
        {
            var eventId = Guid.NewGuid().ToString("N");
            var bytes = Serialize(new
            {
                type = eventType,
                eventId,
                occurredAt = DateTime.UtcNow,
                payload
            });

            var targetSocketIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var pair in _socketTournamentSubscriptions)
            {
                if (pair.Value.ContainsKey(tournamentId))
                {
                    targetSocketIds.Add(pair.Key);
                }
            }

            foreach (var pair in _socketMatchSubscriptions)
            {
                if (pair.Value.ContainsKey(matchId))
                {
                    targetSocketIds.Add(pair.Key);
                }
            }

            foreach (var socketId in _socketVideoFeedSubscriptions.Keys)
            {
                targetSocketIds.Add(socketId);
            }

            await SendToSocketsAsync(targetSocketIds, bytes, eventId, eventType);
        }

        public async Task BroadcastBracketUpdatedAsync(long tournamentId, object payload)
        {
            var eventId = Guid.NewGuid().ToString("N");
            var bytes = Serialize(new
            {
                type = "tournament.bracket.updated",
                eventId,
                occurredAt = DateTime.UtcNow,
                payload
            });

            var targetSocketIds = _socketTournamentSubscriptions
                .Where(pair => pair.Value.ContainsKey(tournamentId))
                .Select(pair => pair.Key)
                .ToArray();

            await SendToSocketsAsync(targetSocketIds, bytes, eventId, "tournament.bracket.updated");
        }

        public async Task BroadcastTournamentPaymentStatusUpdatedAsync(string transactionCode, object payload)
        {
            var normalizedCode = NormalizeTransactionCode(transactionCode);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return;
            }

            var eventId = Guid.NewGuid().ToString("N");
            var bytes = Serialize(new
            {
                type = "tournament.payment.status.updated",
                eventId,
                occurredAt = DateTime.UtcNow,
                payload
            });

            var targetSocketIds = _socketPaymentSubscriptions
                .Where(pair => pair.Value.ContainsKey(normalizedCode))
                .Select(pair => pair.Key)
                .ToArray();

            await SendToSocketsAsync(targetSocketIds, bytes, eventId, "tournament.payment.status.updated");
        }

        private static string NormalizeTransactionCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Concat(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit));
        }

        private static byte[] Serialize(object message)
        {
            var json = JsonSerializer.Serialize(message, JsonOpts);
            return Encoding.UTF8.GetBytes(json);
        }

        private Task SendToSocketsAsync(
            IEnumerable<string> socketIds,
            byte[] bytes,
            string eventId,
            string eventType)
        {
            return Task.WhenAll(socketIds
                .Distinct(StringComparer.Ordinal)
                .Select(socketId => SafeSendAsync(socketId, bytes, eventId, eventType)));
        }

        private async Task SafeSendAsync(string socketId, byte[] bytes, string eventId, string eventType)
        {
            if (!_sockets.TryGetValue(socketId, out var ws)
                || !_socketSendLocks.TryGetValue(socketId, out var sendLock))
            {
                return;
            }

            var lockTaken = false;
            try
            {
                using var timeout = new CancellationTokenSource(SendTimeout);
                await sendLock.WaitAsync(timeout.Token);
                lockTaken = true;

                if (ws.State != WebSocketState.Open)
                {
                    return;
                }

                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug(
                    "Public realtime send timed out for event {EventType}/{EventId} on socket {SocketId}.",
                    eventType,
                    eventId,
                    socketId);

                try
                {
                    ws.Abort();
                }
                catch
                {
                }

                await RemoveSocketAsync(socketId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Public realtime send failed for event {EventType}/{EventId} on socket {SocketId}.",
                    eventType,
                    eventId,
                    socketId);

                try
                {
                    ws.Abort();
                }
                catch
                {
                }

                await RemoveSocketAsync(socketId);
            }
            finally
            {
                if (lockTaken)
                {
                    sendLock.Release();
                }
            }
        }
    }
}
