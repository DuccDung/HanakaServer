using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HanakaServer.Services
{
    public class PublicRealtimeHub
    {
        private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte>> _socketTournamentSubscriptions = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte>> _socketMatchSubscriptions = new();
        private readonly ConcurrentDictionary<string, byte> _socketVideoFeedSubscriptions = new();

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public string AddSocket(WebSocket socket)
        {
            var socketId = Guid.NewGuid().ToString("N");
            _sockets[socketId] = socket;
            _socketTournamentSubscriptions[socketId] = new ConcurrentDictionary<long, byte>();
            _socketMatchSubscriptions[socketId] = new ConcurrentDictionary<long, byte>();
            return socketId;
        }

        public async Task RemoveSocketAsync(string socketId)
        {
            _sockets.TryRemove(socketId, out _);
            _socketTournamentSubscriptions.TryRemove(socketId, out _);
            _socketMatchSubscriptions.TryRemove(socketId, out _);
            _socketVideoFeedSubscriptions.TryRemove(socketId, out _);
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

        public async Task BroadcastMatchScoreUpdatedAsync(long tournamentId, long matchId, object payload)
        {
            var bytes = Serialize(new
            {
                type = "tournament.match.score.updated",
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

            foreach (var socketId in targetSocketIds)
            {
                if (_sockets.TryGetValue(socketId, out var ws))
                {
                    await SafeSendAsync(ws, bytes);
                }
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
                if (ws.State != WebSocketState.Open)
                {
                    return;
                }

                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch
            {
            }
        }
    }
}
