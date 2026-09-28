using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Options;
using HanakaServer.Services;
using HanakaServer.Services.Relay;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using mail_service.Internal;

namespace HanakaServer.Tests;

// Real HTTP, SQL and websocket fixture. Never loads the application's connection string.
internal sealed class CoordinatorStabilityHost : IAsyncDisposable
{
    public const string Password = "Coordinator-stability-test-only!";
    internal const string JwtKey = "public-web-stability-only-signing-key-20260927";
    public required RelaySqlSandbox Sandbox { get; init; }
    public required WebApplication App { get; set; }
    public required string Root { get; init; }
    public required List<FixtureTournament> Tournaments { get; init; }
    public required long[] Coordinators { get; init; }
    public required long[] Referees { get; init; }
    public string Url => App.Urls.Single();
    public string ConnectionString => new SqlConnectionStringBuilder(Sandbox.ConnectionString) { Pooling = true, MaxPoolSize = 100 }.ConnectionString;
    public sealed record FixtureTournament(long Id, long GroupId, long[] Matches, long Team1, long Team2);
    public PickleballDbContext Db() => new(new DbContextOptionsBuilder<PickleballDbContext>().UseSqlServer(ConnectionString).Options);

    public static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "HanakaServer", "HanakaServer.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Run the test from a build output inside the HanakaServer repository.");
    }

    public static async Task<CoordinatorStabilityHost> StartAsync(params int[] sizes)
    {
        var root = RepositoryRoot();
        var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        WebApplication? app = null;
        try
        {
            var connection = new SqlConnectionStringBuilder(sandbox.ConnectionString) { Pooling = true, MaxPoolSize = 100 }.ConnectionString;
            app = BuildApplication(root, connection);
            var tournaments = new List<FixtureTournament>();
            long[] coordinatorIds, refereeIds;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PickleballDbContext>();
                var hash = BCrypt.Net.BCrypt.HashPassword(Password, 4);
                var coordinators = Enumerable.Range(0, 10).Select(i => new User { FullName = $"Điều phối test {i}",
                    Email = $"coordinator{i}@example.test", PasswordHash = hash, IsActive = true }).ToArray();
                var referees = Enumerable.Range(0, 10).Select(i => new User { FullName = $"Trọng tài test {i}",
                    Email = $"referee{i}@example.test", PasswordHash = hash, IsActive = true }).ToArray();
                db.Users.AddRange(coordinators.Concat(referees));
                var coordinatorRole = await db.Roles.SingleOrDefaultAsync(r => r.RoleCode == RoleCodes.Coordinator)
                    ?? new Role { RoleCode = RoleCodes.Coordinator, RoleName = "Điều phối" };
                if (coordinatorRole.RoleId == 0) db.Roles.Add(coordinatorRole);
                var refereeRole = new Role { RoleCode = RoleCodes.Referee, RoleName = "Trọng tài" };
                db.Roles.Add(refereeRole);
                await db.SaveChangesAsync();
                db.UserRoles.AddRange(coordinators.Select(u => new UserRole { UserId = u.UserId, RoleId = coordinatorRole.RoleId, CreatedAt = DateTime.UtcNow }));
                db.UserRoles.AddRange(referees.Select(u => new UserRole { UserId = u.UserId, RoleId = refereeRole.RoleId, CreatedAt = DateTime.UtcNow }));
                foreach (var size in sizes)
                {
                    var tournament = new Tournament { Title = $"STABILITY TEST {size} trận", Status = "OPEN", GameType = "DOUBLE", GenderCategory = "OPEN" };
                    var group = new TournamentRoundGroup { GroupName = "Bảng kiểm thử", TournamentRoundMap = new() {
                        Tournament = tournament, RoundKey = "R1", RoundLabel = "Vòng 1" } };
                    var a = new TournamentRegistration { Tournament = tournament, RegCode = $"{size}-A", Player1Name = "Đội Hanaka A", Success = true };
                    var b = new TournamentRegistration { Tournament = tournament, RegCode = $"{size}-B", Player1Name = "Đội Hanaka B", Success = true };
                    db.TournamentRoundGroups.Add(group); db.TournamentRegistrations.AddRange(a, b);
                    await db.SaveChangesAsync();
                    var matches = Enumerable.Range(0, size).Select(i => new TournamentGroupMatch {
                        TournamentId = tournament.TournamentId, TournamentRoundGroupId = group.TournamentRoundGroupId,
                        Team1RegistrationId = a.RegistrationId,
                        Team2Registration = i == 0 ? b : new TournamentRegistration { TournamentId = tournament.TournamentId,
                            RegCode = $"{size}-B{i}", Player1Name = $"Đội đối thủ {i}", Success = true },
                        RefereeUserId = referees[i % referees.Length].UserId, StartAt = DateTime.Today.AddHours(8), CourtText = $"Sân {i % 10 + 1}" }).ToArray();
                    db.TournamentGroupMatches.AddRange(matches);
                    db.TournamentCoordinators.AddRange(coordinators.Select(u => new TournamentCoordinator {
                        UserId = u.UserId, TournamentId = tournament.TournamentId, CreatedAt = DateTime.UtcNow }));
                    await db.SaveChangesAsync();
                    tournaments.Add(new(tournament.TournamentId, group.TournamentRoundGroupId, matches.Select(m => m.MatchId).ToArray(), a.RegistrationId, b.RegistrationId));
                }
                coordinatorIds = coordinators.Select(u => u.UserId).ToArray(); refereeIds = referees.Select(u => u.UserId).ToArray();
            }
            await app.StartAsync();
            return new() { App = app, Sandbox = sandbox, Root = root, Tournaments = tournaments, Coordinators = coordinatorIds, Referees = refereeIds };
        }
        catch { if (app != null) await app.DisposeAsync(); await sandbox.DisposeAsync(); throw; }
    }

    private static WebApplication BuildApplication(string root, string connection)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = "Testing", ApplicationName = typeof(CoordinatorPortalController).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory, WebRootPath = Path.Combine(root, "HanakaServer", "wwwroot") });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Coordination:Enabled"] = "true", ["Jwt:Key"] = JwtKey,
            ["Jwt:Issuer"] = "coordination-test", ["Jwt:Audience"] = "coordination-test" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<PickleballDbContext>(o => o.UseSqlServer(connection));
        builder.Services.AddSingleton<PublicRealtimeHub>();
        builder.Services.AddSingleton<RealtimeHub>();
        builder.Services.AddScoped<PublicWebSocketHandler>();
        builder.Services.AddScoped<MatchCoordinationService>();
        builder.Services.AddScoped<TournamentUserNotificationService>();
        builder.Services.AddScoped<ITournamentStandingsService, TournamentStandingsService>();
        builder.Services.AddScoped<ITournamentBracketPropagationService, TournamentBracketPropagationService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<RelayOptions>(o => o.AdminPreviewEnabled = true);
        builder.Services.AddScoped<RelayMatchLineupSnapshotService>();
        builder.Services.AddScoped<RelayScoringService>();
        builder.Services.AddScoped<RelayLegacyWriteGuard>();
        builder.Services.AddScoped<RelayTeamReader>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAppAuthService, AppAuthService>();
        builder.Services.AddScoped<IWebAuthCookieService, WebAuthCookieService>();
        builder.Services.AddSingleton<IOtpDeliveryService, DisabledOtp>();
        builder.Services.AddSingleton<IUserOtpService, DisabledOtp>();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie().AddCoordinatorPortal()
            .AddJwtBearer(options => {
                options.TokenValidationParameters = new() { ValidIssuer = "coordination-test", ValidAudience = "coordination-test",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)), ClockSkew = TimeSpan.Zero };
                // Use the same bearer/cookie precedence as the production application.
                options.Events = new JwtBearerEvents { OnMessageReceived = context => {
                    var header = context.Request.Headers.Authorization.ToString();
                    if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(header[7..]))
                        context.Token = header[7..].Trim();
                    else if (context.Request.Cookies.TryGetValue(WebAuthCookieService.AccessTokenCookieName, out var cookie))
                        context.Token = cookie;
                    return Task.CompletedTask;
                } };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(CoordinatorPortalController).Assembly);
        var app = builder.Build();
        app.UseMiddleware<HanakaServer.Middleware.RequestCancellationMiddleware>();
        app.UseMiddleware<HanakaServer.Middleware.MatchConcurrencyMiddleware>();
        app.UseStaticFiles(); app.UseWebSockets(); app.UseAuthentication(); app.UseAuthorization();
        app.MapControllers(); app.MapControllerRoute("default", "{controller=Home}/{action=Login}/{id?}");
        app.Map("/ws-public", async context => {
            if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await context.RequestServices.GetRequiredService<PublicWebSocketHandler>().HandleAsync(socket, context.RequestAborted);
        });
        return app;
    }

    public async Task RestartAsync()
    {
        var address = Url;
        await App.StopAsync(); await App.DisposeAsync();
        App = BuildApplication(Root, ConnectionString);
        App.Urls.Clear(); App.Urls.Add(address);
        await App.StartAsync();
    }

    public HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(Url), Timeout = TimeSpan.FromSeconds(40) };
    public async Task<(HttpClient Client, string Token)> LoginWebCoordinator(int index = 0)
    {
        var client = Client();
        try
        {
            using var login = await client.PostAsJsonAsync("/api/web-auth/login",
                new { identifier = $"coordinator{index}@example.test", password = Password });
            login.EnsureSuccessStatusCode();
            var permissions = await client.GetFromJsonAsync<JsonElement>($"/api/coordination/tournaments/{Tournaments[0].Id}/permissions");
            return (client, permissions.GetProperty("requestToken").GetString()!);
        }
        catch { client.Dispose(); throw; }
    }

    // Login/session use the real AppAuthService. OTP must never contact external services in this fixture.
    private sealed class DisabledOtp : IOtpDeliveryService, IUserOtpService
    {
        public Task<OtpDeliveryResult> SendRegistrationOtpAsync(User user, string otp, CancellationToken ct = default) => throw new InvalidOperationException("OTP is disabled in stability tests.");
        public Task<OtpDeliveryResult> SendPasswordResetOtpAsync(User user, string otp, CancellationToken ct = default) => throw new InvalidOperationException("OTP is disabled in stability tests.");
        public Task<(string otp, DateTime expiredAtUtc)> CreateOtpAsync(User user, CancellationToken ct = default) => throw new InvalidOperationException("OTP is disabled in stability tests.");
        public Task InvalidateActiveOtpsAsync(long userId, string email, CancellationToken ct = default) => throw new InvalidOperationException("OTP is disabled in stability tests.");
    }
    public async Task<(HttpClient Client, string Token)> LoginCoordinator(int index = 0)
    {
        var client = Client();
        try
        {
            var token = await Token(client, "/CoordinatorPortal/Login");
            using var response = await Write(client, HttpMethod.Post, "/api/coordinator-portal/login",
                new { account = $"coordinator{index}@example.test", password = Password }, token);
            response.EnsureSuccessStatusCode();
            return (client, await Token(client, "/CoordinatorPortal/Matches"));
        }
        catch { client.Dispose(); throw; }
    }
    public async Task<HttpClient> LoginReferee(int index = 0)
    {
        var client = Client();
        using var response = await client.PostAsJsonAsync("/api/referee-auth/login", new { email = $"referee{index}@example.test", password = Password });
        response.EnsureSuccessStatusCode(); return client;
    }
    public static async Task<string> Token(HttpClient client, string page)
    {
        using var response = await client.GetAsync(page); response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Razor page did not issue an antiforgery token.");
        return token;
    }
    public static async Task<HttpResponseMessage> Write(HttpClient client, HttpMethod method, string path, object value, string token, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(value) };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request, ct);
    }
    public static JsonElement[] Matches(JsonElement schedule) => schedule.GetProperty("rounds").EnumerateArray()
        .SelectMany(r => r.GetProperty("groups").EnumerateArray()).SelectMany(g => g.GetProperty("matches").EnumerateArray()).ToArray();
    public async ValueTask DisposeAsync()
    {
        await App.StopAsync(); await App.DisposeAsync();
        using var pool = new SqlConnection(ConnectionString); SqlConnection.ClearPool(pool);
        await Sandbox.DisposeAsync();
    }
}
