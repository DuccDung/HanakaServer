using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HanakaServer.Tests;

public sealed class CoordinatorSoakFactAttribute : FactAttribute
{
    public CoordinatorSoakFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("HANAKA_COORDINATION_SOAK") != "1")
            Skip = "Opt in with HANAKA_COORDINATION_SOAK=1. Real local HTTP/SQL/WebSocket load; default duration 120 minutes.";
    }
}

public sealed class CoordinatorSoakTests
{
    [CoordinatorSoakFact]
    public async Task Two_hour_real_http_sql_and_two_hundred_viewer_soak()
    {
        var minutes = int.TryParse(Environment.GetEnvironmentVariable("HANAKA_COORDINATION_SOAK_MINUTES"), out var value) ? Math.Clamp(value, 1, 240) : 120;
        await using var host = await CoordinatorStabilityHost.StartAsync(100, 500, 2000);
        var artifact = Path.Combine(host.Root, "artifacts", "coordination-stability", "soak-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(artifact);
        var manifest = new { host.Url, pid = Environment.ProcessId, startedAt = DateTime.UtcNow, minutes,
            tournaments = host.Tournaments, coordinator = "coordinator0@example.test", referee = "referee0@example.test", password = CoordinatorStabilityHost.Password };
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(artifact, "host.json"), JsonSerializer.Serialize(manifest, jsonOptions));
        await File.WriteAllTextAsync(Path.Combine(host.Root, "artifacts", "coordination-stability", "active-host.json"), JsonSerializer.Serialize(manifest, jsonOptions));
        var coordinators = await Task.WhenAll(Enumerable.Range(0, 10).Select(host.LoginCoordinator));
        var referees = await Task.WhenAll(Enumerable.Range(0, 10).Select(host.LoginReferee));
        using var viewerClient = host.Client();
        Histogram writeLatency = new(), readLatency = new(), deliveryLatency = new();
        var expected = new ConcurrentDictionary<long, long>();
        var errors = new ConcurrentQueue<string>();
        var viewers = new List<SoakViewer>();
        var samples = new List<object>();
        var stopwatch = Stopwatch.StartNew();
        var phase = -1; var cycle = 0; long lastSample = -1;
        try
        {
            while (stopwatch.Elapsed < TimeSpan.FromMinutes(minutes))
            {
                var nextPhase = Math.Min(2, (int)(stopwatch.Elapsed.TotalMinutes / (minutes / 3.0)));
                var fixture = host.Tournaments[nextPhase];
                if (phase != nextPhase)
                {
                    foreach (var viewer in viewers) await viewer.DisposeAsync(); viewers.Clear(); expected.Clear();
                    phase = nextPhase;
                    // Bootstrap real schedule reads in bounded waves, followed by 200 live websocket subscriptions.
                    using var slots = new SemaphoreSlim(10);
                    using var bootstrap = new CancellationTokenSource(TimeSpan.FromSeconds(70));
                    var connected = new ConcurrentBag<SoakViewer>();
                    try
                    {
                        await Task.WhenAll(Enumerable.Range(0, 200).Select(async _ => {
                            await slots.WaitAsync(bootstrap.Token);
                            try
                            {
                                var before = Stopwatch.GetTimestamp();
                                var schedule = await viewerClient.GetFromJsonAsync<JsonElement>($"/api/tournaments/{fixture.Id}/rounds-with-matches", bootstrap.Token);
                                readLatency.Record(Stopwatch.GetElapsedTime(before).TotalMilliseconds);
                                Assert.Equal(fixture.Matches.Length, CoordinatorStabilityHost.Matches(schedule).Length);
                                connected.Add(await SoakViewer.Connect(host.Url, fixture.Id, deliveryLatency, errors, schedule));
                            }
                            catch { bootstrap.Cancel(); throw; }
                            finally { slots.Release(); }
                        }));
                        viewers.AddRange(connected);
                    }
                    catch { foreach (var viewer in connected) await viewer.DisposeAsync(); throw; }
                }
                cycle++;
                await Task.WhenAll(Enumerable.Range(0, 10).Select(async i => {
                    try
                    {
                        var matchId = fixture.Matches[i];
                        long version;
                        await using (var db = host.Db()) version = await db.TournamentGroupMatches.Where(m => m.MatchId == matchId).Select(m => m.StateVersion).SingleAsync();
                        var before = Stopwatch.GetTimestamp();
                        using var response = await CoordinatorStabilityHost.Write(coordinators[i].Client, HttpMethod.Put,
                            $"/api/coordinator-portal/matches/{matchId}", new { expectedVersion = version, courtText = $"Sân {i + 1} / {cycle}", preparing = cycle % 2 == 1 }, coordinators[i].Token);
                        writeLatency.Record(Stopwatch.GetElapsedTime(before).TotalMilliseconds);
                        response.EnsureSuccessStatusCode();
                        var snapshot = await response.Content.ReadFromJsonAsync<JsonElement>(); expected[matchId] = snapshot.GetProperty("stateVersion").GetInt64();
                    }
                    catch (Exception error) { errors.Enqueue("dispatch: " + error.Message); }
                }).Concat(Enumerable.Range(0, 10).Select(async i => {
                    try
                    {
                        var matchId = fixture.Matches[i + 10]; var before = Stopwatch.GetTimestamp();
                        using var response = await referees[i].PutAsJsonAsync($"/api/referee/matches/{matchId}/score", new { scoreTeam1 = cycle % 64, scoreTeam2 = cycle % 43, isCompleted = false });
                        writeLatency.Record(Stopwatch.GetElapsedTime(before).TotalMilliseconds); response.EnsureSuccessStatusCode();
                        var snapshot = await response.Content.ReadFromJsonAsync<JsonElement>(); expected[matchId] = snapshot.GetProperty("stateVersion").GetInt64();
                    }
                    catch (Exception error) { errors.Enqueue("score: " + error.Message); }
                })));
                // Check delivery to every viewer, not just aggregate message counts.
                var until = Stopwatch.StartNew();
                while (until.Elapsed < TimeSpan.FromSeconds(5) && viewers.Any(v => expected.Any(e => v.Versions.GetValueOrDefault(e.Key) < e.Value))) await Task.Delay(50);
                if (viewers.Any(v => expected.Any(e => v.Versions.GetValueOrDefault(e.Key) < e.Value))) errors.Enqueue("viewer did not converge within 5 seconds");
                if ((long)stopwatch.Elapsed.TotalMinutes != lastSample)
                {
                    lastSample = (long)stopwatch.Elapsed.TotalMinutes;
                    // Portal operators poll schedule and live permissions, with real cookie validation and SQL queries.
                    await Task.WhenAll(coordinators.Select(async operatorClient => {
                        var before = Stopwatch.GetTimestamp();
                        using var session = await operatorClient.Client.GetAsync("/api/coordinator-portal/session"); session.EnsureSuccessStatusCode();
                        var schedule = await operatorClient.Client.GetFromJsonAsync<JsonElement>($"/api/tournaments/{fixture.Id}/rounds-with-matches");
                        readLatency.Record(Stopwatch.GetElapsedTime(before).TotalMilliseconds);
                        Assert.Equal(fixture.Matches.Length, CoordinatorStabilityHost.Matches(schedule).Length);
                    }));
                    for (var i = 0; i < 5; i++)
                    {
                        var index = (cycle + i) % viewers.Count; await viewers[index].DisposeAsync();
                        var schedule = await viewerClient.GetFromJsonAsync<JsonElement>($"/api/tournaments/{fixture.Id}/rounds-with-matches");
                        viewers[index] = await SoakViewer.Connect(host.Url, fixture.Id, deliveryLatency, errors, schedule);
                    }
                    using var process = Process.GetCurrentProcess();
                    await using var connection = new SqlConnection(host.ConnectionString); await connection.OpenAsync();
                    using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id = DB_ID()";
                    var sqlConnections = Convert.ToInt32(await command.ExecuteScalarAsync());
                    var sample = new { elapsedSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds), phase, matches = fixture.Matches.Length,
                        viewers = viewers.Count, cycle, managedBytes = GC.GetTotalMemory(false), processWorkingSetBytes = process.WorkingSet64,
                        processCpuSeconds = process.TotalProcessorTime.TotalSeconds, sqlConnections, write = writeLatency.Snapshot(),
                        read = readLatency.Snapshot(), delivery = deliveryLatency.Snapshot(), errors = errors.Count };
                    samples.Add(sample);
                    await File.AppendAllTextAsync(Path.Combine(artifact, "samples.jsonl"), JsonSerializer.Serialize(sample) + Environment.NewLine);
                    Console.WriteLine($"Coordinator soak: {stopwatch.Elapsed.TotalMinutes:F1}/{minutes} min, {fixture.Matches.Length} matches, {viewers.Count} viewers, errors={errors.Count}");
                }
                if (errors.Count > 25) break;
                var remaining = TimeSpan.FromMinutes(minutes) - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining < TimeSpan.FromSeconds(15) ? remaining : TimeSpan.FromSeconds(15));
            }
        }
        finally
        {
            foreach (var viewer in viewers) await viewer.DisposeAsync();
            foreach (var coordinator in coordinators) coordinator.Client.Dispose(); foreach (var referee in referees) referee.Dispose();
            var report = new { requestedMinutes = minutes, elapsedSeconds = stopwatch.Elapsed.TotalSeconds, cycle,
                write = writeLatency.Snapshot(), read = readLatency.Snapshot(), delivery = deliveryLatency.Snapshot(), errors = errors.ToArray(), samples };
            await File.WriteAllTextAsync(Path.Combine(artifact, "result.json"), JsonSerializer.Serialize(report, jsonOptions));
            await File.WriteAllTextAsync(Path.Combine(host.Root, "artifacts", "coordination-stability", "active-host.json"), JsonSerializer.Serialize(new { stopped = true, artifact }, jsonOptions));
        }
        Assert.Empty(errors);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMinutes(minutes));
        Assert.True(writeLatency.Percentile(.95) < 1000, $"Write p95 {writeLatency.Percentile(.95)} ms exceeded 1 second.");
        Assert.True(deliveryLatency.Percentile(.95) < 2000, $"Websocket p95 {deliveryLatency.Percentile(.95)} ms exceeded 2 seconds.");
    }

    private sealed class Histogram
    {
        private readonly long[] buckets = new long[60001];
        public void Record(double milliseconds) => Interlocked.Increment(ref buckets[Math.Clamp((int)Math.Ceiling(milliseconds), 0, buckets.Length - 1)]);
        public long Count => buckets.Sum();
        public int Percentile(double percentile) { var target = (long)Math.Ceiling(Count * percentile); long count = 0; for (var i = 0; i < buckets.Length; i++) { count += Interlocked.Read(ref buckets[i]); if (count >= target) return i; } return buckets.Length - 1; }
        public object Snapshot() => new { count = Count, p50Ms = Percentile(.5), p95Ms = Percentile(.95), p99Ms = Percentile(.99) };
    }
    private sealed class SoakViewer : IAsyncDisposable
    {
        private readonly ClientWebSocket socket = new();
        private readonly CancellationTokenSource stop = new();
        private Task? receiver;
        public ConcurrentDictionary<long, long> Versions { get; } = new();
        public static async Task<SoakViewer> Connect(string url, long tournamentId, Histogram lag, ConcurrentQueue<string> errors, JsonElement schedule)
        {
            var viewer = new SoakViewer();
            foreach (var match in CoordinatorStabilityHost.Matches(schedule)) viewer.Versions[match.GetProperty("matchId").GetInt64()] = match.GetProperty("stateVersion").GetInt64();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await CoordinatorStabilityTests.Subscribe(viewer.socket, url, tournamentId, timeout.Token);
            viewer.receiver = viewer.Read(lag, errors); return viewer;
        }
        private async Task Read(Histogram lag, ConcurrentQueue<string> errors)
        {
            try
            {
                var buffer = new byte[16384];
                while (!stop.IsCancellationRequested)
                {
                    using var stream = new MemoryStream(); WebSocketReceiveResult part;
                    do { part = await socket.ReceiveAsync(buffer, stop.Token); if (part.MessageType == WebSocketMessageType.Close) throw new IOException("unexpected websocket closure"); stream.Write(buffer, 0, part.Count); } while (!part.EndOfMessage);
                    using var json = JsonDocument.Parse(stream.ToArray()); var root = json.RootElement;
                    if (!root.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("matchId", out var id) || !payload.TryGetProperty("stateVersion", out var version)) continue;
                    Versions.AddOrUpdate(id.GetInt64(), version.GetInt64(), (_, current) => Math.Max(current, version.GetInt64()));
                    if (root.TryGetProperty("occurredAt", out var at)) lag.Record((DateTimeOffset.UtcNow - at.GetDateTimeOffset()).TotalMilliseconds);
                }
            }
            catch (Exception error) { if (!stop.IsCancellationRequested) errors.Enqueue("viewer: " + error.Message); }
        }
        public async ValueTask DisposeAsync() { stop.Cancel(); socket.Abort(); if (receiver != null) await receiver; socket.Dispose(); stop.Dispose(); }
    }
}
