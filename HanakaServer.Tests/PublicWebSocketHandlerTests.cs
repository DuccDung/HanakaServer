using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using HanakaServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public class PublicWebSocketHandlerTests
{
    [Fact]
    public async Task TournamentSubscribe_ReceivesAckAndBracketBroadcast()
    {
        var hub = CreateHub();
        var handler = new PublicWebSocketHandler(hub);
        using var socket = new ScriptedWebSocket();

        var handlerTask = handler.HandleAsync(socket, CancellationToken.None);
        await socket.WaitForMessageAsync("hello.public");

        socket.QueueText(new { type = "tournament.subscribe", tournamentId = 42 });
        await socket.WaitForMessageAsync("tournament.subscribed");

        await hub.BroadcastBracketUpdatedAsync(42, new
        {
            TournamentId = 42,
            SourceMatchId = 7
        });

        var broadcast = await socket.WaitForMessageAsync("tournament.bracket.updated");
        Assert.Equal(42, broadcast.RootElement.GetProperty("payload").GetProperty("tournamentId").GetInt64());
        Assert.Equal(7, broadcast.RootElement.GetProperty("payload").GetProperty("sourceMatchId").GetInt64());

        socket.QueueClose();
        await handlerTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MatchSubscribe_ReceivesScore_ThenUnsubscribeStopsDelivery()
    {
        var hub = CreateHub();
        var handler = new PublicWebSocketHandler(hub);
        using var socket = new ScriptedWebSocket();

        var handlerTask = handler.HandleAsync(socket, CancellationToken.None);
        await socket.WaitForMessageAsync("hello.public");

        socket.QueueText(new { type = "match.subscribe", matchId = 77 });
        await socket.WaitForMessageAsync("match.subscribed");

        await hub.BroadcastMatchScoreUpdatedAsync(9, 77, new
        {
            TournamentId = 9,
            MatchId = 77,
            ScoreTeam1 = 5,
            ScoreTeam2 = 3
        });

        var scoreEvent = await socket.WaitForMessageAsync("tournament.match.score.updated");
        Assert.Equal(5, scoreEvent.RootElement.GetProperty("payload").GetProperty("scoreTeam1").GetInt32());

        socket.QueueText(new { type = "match.unsubscribe", matchId = 77 });
        await socket.WaitForMessageAsync("match.unsubscribed");
        var scoreCountBefore = socket.CountMessages("tournament.match.score.updated");

        await hub.BroadcastMatchScoreUpdatedAsync(9, 77, new
        {
            TournamentId = 9,
            MatchId = 77,
            ScoreTeam1 = 8,
            ScoreTeam2 = 6
        });

        await Task.Delay(75);
        Assert.Equal(scoreCountBefore, socket.CountMessages("tournament.match.score.updated"));

        socket.QueueClose();
        await handlerTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task PingAndVideoSubscription_UseSameSerializedSendPath()
    {
        var hub = CreateHub();
        var handler = new PublicWebSocketHandler(hub);
        using var socket = new ScriptedWebSocket(sendDelayMs: 15);

        var handlerTask = handler.HandleAsync(socket, CancellationToken.None);
        await socket.WaitForMessageAsync("hello.public");

        socket.QueueText(new { type = "videos.subscribe" });
        await socket.WaitForMessageAsync("videos.subscribed");
        socket.QueueText(new { type = "ping" });

        await Task.WhenAll(
            socket.WaitForMessageAsync("pong"),
            hub.BroadcastMatchScoreUpdatedAsync(3, 4, new
            {
                TournamentId = 3,
                MatchId = 4,
                ScoreTeam1 = 11,
                ScoreTeam2 = 9
            }));

        await socket.WaitForMessageAsync("tournament.match.score.updated");
        Assert.False(socket.ConcurrentSendDetected);

        socket.QueueClose();
        await handlerTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static PublicRealtimeHub CreateHub() =>
        new(NullLogger<PublicRealtimeHub>.Instance);

    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Channel<IncomingFrame> _incoming = Channel.CreateUnbounded<IncomingFrame>();
        private readonly ConcurrentQueue<string> _messages = new();
        private readonly int _sendDelayMs;
        private WebSocketState _state = WebSocketState.Open;
        private WebSocketCloseStatus? _closeStatus;
        private string? _closeDescription;
        private int _activeSends;

        public ScriptedWebSocket(int sendDelayMs = 0)
        {
            _sendDelayMs = sendDelayMs;
        }

        public bool ConcurrentSendDetected { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => _closeStatus;
        public override string? CloseStatusDescription => _closeDescription;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public void QueueText(object message)
        {
            var json = JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            _incoming.Writer.TryWrite(new IncomingFrame(
                Encoding.UTF8.GetBytes(json),
                WebSocketMessageType.Text,
                true));
        }

        public void QueueClose()
        {
            _incoming.Writer.TryWrite(new IncomingFrame([], WebSocketMessageType.Close, true));
        }

        public int CountMessages(string type) =>
            _messages.Count(message => GetMessageType(message) == type);

        public async Task<JsonDocument> WaitForMessageAsync(string type)
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                var match = _messages.FirstOrDefault(message => GetMessageType(message) == type);
                if (match != null)
                {
                    return JsonDocument.Parse(match);
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"Did not receive public WebSocket message '{type}'.");
        }

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _closeStatus = closeStatus;
            _closeDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _closeStatus = closeStatus;
            _closeDescription = statusDescription;
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _state = WebSocketState.Closed;
            _incoming.Writer.TryComplete();
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            var frame = await _incoming.Reader.ReadAsync(cancellationToken);
            if (frame.Bytes.Length > buffer.Count)
            {
                throw new InvalidOperationException("The scripted WebSocket test frame exceeds the receive buffer.");
            }

            frame.Bytes.CopyTo(buffer.AsSpan());
            if (frame.MessageType == WebSocketMessageType.Close)
            {
                _state = WebSocketState.CloseReceived;
            }

            return new WebSocketReceiveResult(frame.Bytes.Length, frame.MessageType, frame.EndOfMessage);
        }

        public override async Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
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

                _messages.Enqueue(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            }
            finally
            {
                Interlocked.Decrement(ref _activeSends);
            }
        }

        private static string? GetMessageType(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.TryGetProperty("type", out var type)
                    ? type.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private sealed record IncomingFrame(
            byte[] Bytes,
            WebSocketMessageType MessageType,
            bool EndOfMessage);
    }
}
