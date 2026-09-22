using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HanakaServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public class PublicRealtimeHubTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchBroadcast_TargetsTournamentOrMatchSubscribers_WithoutDuplicates(bool coordination)
    {
        var hub = CreateHub();
        var tournamentSocket = new RecordingWebSocket();
        var matchSocket = new RecordingWebSocket();
        var bothSocket = new RecordingWebSocket();
        var unrelatedSocket = new RecordingWebSocket();

        var tournamentSocketId = hub.AddSocket(tournamentSocket);
        var matchSocketId = hub.AddSocket(matchSocket);
        var bothSocketId = hub.AddSocket(bothSocket);
        hub.AddSocket(unrelatedSocket);

        hub.SubscribeTournament(tournamentSocketId, 12);
        hub.SubscribeMatch(matchSocketId, 34);
        hub.SubscribeTournament(bothSocketId, 12);
        hub.SubscribeMatch(bothSocketId, 34);

        var payload = new
        {
            TournamentId = 12,
            MatchId = 34,
            ScoreTeam1 = 11,
            ScoreTeam2 = 7
        };
        if (coordination) await hub.BroadcastMatchCoordinationUpdatedAsync(12, 34, payload);
        else await hub.BroadcastMatchScoreUpdatedAsync(12, 34, payload);

        Assert.Single(tournamentSocket.Messages);
        Assert.Single(matchSocket.Messages);
        Assert.Single(bothSocket.Messages);
        Assert.Empty(unrelatedSocket.Messages);

        using var message = JsonDocument.Parse(tournamentSocket.Messages.Single());
        Assert.Equal(coordination ? "tournament.match.coordination.updated" : "tournament.match.score.updated", message.RootElement.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(message.RootElement.GetProperty("eventId").GetString()));
        Assert.Equal(JsonValueKind.String, message.RootElement.GetProperty("occurredAt").ValueKind);
        Assert.Equal(34, message.RootElement.GetProperty("payload").GetProperty("matchId").GetInt64());
    }

    [Fact]
    public async Task BracketBroadcast_TargetsOnlyTournamentSubscribers()
    {
        var hub = CreateHub();
        var tournamentSocket = new RecordingWebSocket();
        var matchOnlySocket = new RecordingWebSocket();
        var otherTournamentSocket = new RecordingWebSocket();

        var tournamentSocketId = hub.AddSocket(tournamentSocket);
        var matchOnlySocketId = hub.AddSocket(matchOnlySocket);
        var otherTournamentSocketId = hub.AddSocket(otherTournamentSocket);

        hub.SubscribeTournament(tournamentSocketId, 55);
        hub.SubscribeMatch(matchOnlySocketId, 99);
        hub.SubscribeTournament(otherTournamentSocketId, 56);

        await hub.BroadcastBracketUpdatedAsync(55, new
        {
            TournamentId = 55,
            SourceMatchId = 99,
            Reason = "MATCH_COMPLETED"
        });

        Assert.Single(tournamentSocket.Messages);
        Assert.Empty(matchOnlySocket.Messages);
        Assert.Empty(otherTournamentSocket.Messages);

        using var message = JsonDocument.Parse(tournamentSocket.Messages.Single());
        Assert.Equal("tournament.bracket.updated", message.RootElement.GetProperty("type").GetString());
        Assert.Equal(55, message.RootElement.GetProperty("payload").GetProperty("tournamentId").GetInt64());
        Assert.Equal(99, message.RootElement.GetProperty("payload").GetProperty("sourceMatchId").GetInt64());
    }

    [Fact]
    public async Task UnsubscribedSocket_DoesNotReceiveLaterBroadcasts()
    {
        var hub = CreateHub();
        var socket = new RecordingWebSocket();
        var socketId = hub.AddSocket(socket);

        hub.SubscribeTournament(socketId, 88);
        hub.UnsubscribeTournament(socketId, 88);

        await hub.BroadcastBracketUpdatedAsync(88, new { TournamentId = 88 });

        Assert.Empty(socket.Messages);
    }

    [Fact]
    public async Task ConcurrentBroadcasts_AreSerializedPerSocket()
    {
        var hub = CreateHub();
        var socket = new RecordingWebSocket(sendDelayMs: 25);
        var socketId = hub.AddSocket(socket);
        hub.SubscribeTournament(socketId, 101);

        await Task.WhenAll(
            hub.BroadcastMatchScoreUpdatedAsync(101, 1, new { TournamentId = 101, MatchId = 1 }),
            hub.BroadcastBracketUpdatedAsync(101, new { TournamentId = 101, SourceMatchId = 1 }));

        Assert.Equal(2, socket.Messages.Count);
        Assert.False(socket.ConcurrentSendDetected);
    }

    [Fact]
    public async Task FailedSocket_DoesNotPreventDelivery_AndIsRemoved()
    {
        var hub = CreateHub();
        var failedSocket = new RecordingWebSocket(throwOnSend: true);
        var healthySocket = new RecordingWebSocket();
        var failedSocketId = hub.AddSocket(failedSocket);
        var healthySocketId = hub.AddSocket(healthySocket);
        hub.SubscribeTournament(failedSocketId, 202);
        hub.SubscribeTournament(healthySocketId, 202);

        await hub.BroadcastBracketUpdatedAsync(202, new { TournamentId = 202 });
        await hub.BroadcastBracketUpdatedAsync(202, new { TournamentId = 202 });

        Assert.Equal(WebSocketState.Aborted, failedSocket.State);
        Assert.Equal(1, failedSocket.SendAttempts);
        Assert.Equal(2, healthySocket.Messages.Count);
    }

    private static PublicRealtimeHub CreateHub() =>
        new(NullLogger<PublicRealtimeHub>.Instance);

    private sealed class RecordingWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        private WebSocketCloseStatus? _closeStatus;
        private string? _closeStatusDescription;
        private readonly int _sendDelayMs;
        private readonly bool _throwOnSend;
        private int _activeSends;

        public RecordingWebSocket(int sendDelayMs = 0, bool throwOnSend = false)
        {
            _sendDelayMs = sendDelayMs;
            _throwOnSend = throwOnSend;
        }

        public ConcurrentQueue<string> Messages { get; } = new();
        public bool ConcurrentSendDetected { get; private set; }
        public int SendAttempts { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => _closeStatus;
        public override string? CloseStatusDescription => _closeStatusDescription;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));

        public override async Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            SendAttempts += 1;
            if (_throwOnSend)
            {
                throw new WebSocketException("Simulated send failure.");
            }

            if (Interlocked.Increment(ref _activeSends) > 1)
            {
                ConcurrentSendDetected = true;
            }

            try
            {
                if (_sendDelayMs > 0)
                {
                    await Task.Delay(_sendDelayMs, cancellationToken);
                }
                Messages.Enqueue(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            }
            finally
            {
                Interlocked.Decrement(ref _activeSends);
            }
        }
    }
}
