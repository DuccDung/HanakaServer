using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HanakaServer.Tests;

public sealed class CoordinatorPortalTests
{
    [Fact]
    public async Task Dedicated_cookie_login_csrf_live_permissions_and_referee_handoff_work_over_http()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = "Testing", ApplicationName = typeof(CoordinatorPortalController).Assembly.GetName().Name });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var database = "coordinator-portal-" + Guid.NewGuid();
        builder.Services.AddDbContext<PickleballDbContext>(options => options.UseInMemoryDatabase(database)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddSingleton<PublicRealtimeHub>();
        builder.Services.AddScoped<MatchCoordinationService>();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie().AddCoordinatorPortal();
        builder.Services.AddAuthorization();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(CoordinatorPortalController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapControllers(); app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        app.MapPost("/test/default-cookie", async (HttpContext context) => {
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1"),
                    new Claim(ClaimTypes.Role, RoleCodes.Coordinator)], CookieAuthenticationDefaults.AuthenticationScheme)));
        });
        long userId, tournamentId, otherTournamentId, matchId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballDbContext>();
            var user = new User { FullName = "Điều phối A", Email = "coordinator@example.test", Phone = "0900000000",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test-only-password", 4), IsActive = true };
            var noAssignment = new User { FullName = "Chưa phân công", Email = "unassigned@example.test",
                PasswordHash = user.PasswordHash, IsActive = true };
            var tournament = new Tournament { Title = "Giải được phân công", Status = "OPEN", GameType = "DOUBLE", GenderCategory = "OPEN" };
            var other = new Tournament { Title = "Giải khác", Status = "OPEN", GameType = "DOUBLE", GenderCategory = "OPEN" };
            var group = new TournamentRoundGroup { GroupName = "A", TournamentRoundMap = new() {
                Tournament = tournament, RoundKey = "R1", RoundLabel = "Vòng 1" } };
            var match = new TournamentGroupMatch { Tournament = tournament, TournamentRoundGroup = group,
                Team1Registration = new() { Tournament = tournament, RegCode = "A", Player1Name = "Đội A" },
                Team2Registration = new() { Tournament = tournament, RegCode = "B", Player1Name = "Đội B" } };
            db.Users.AddRange(user, noAssignment); db.Tournaments.Add(other); db.TournamentGroupMatches.Add(match);
            await db.SaveChangesAsync();
            await new AdminTournamentCoordinatorsController(db).Assign(tournament.TournamentId, user.UserId, default);
            // Keep the global role after removing the first tournament assignment to test per-tournament revocation.
            await new AdminTournamentCoordinatorsController(db).Assign(other.TournamentId, user.UserId, default);
            userId = user.UserId; tournamentId = tournament.TournamentId; otherTournamentId = other.TournamentId; matchId = match.MatchId;
        }
        await app.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) };
        async Task<string> Token(string page)
        {
            var response = await client.GetAsync(page); response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("coordinator-portal.css", html);
            var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(token); return WebUtility.HtmlDecode(token);
        }
        async Task<HttpResponseMessage> Write(HttpMethod method, string path, object body, string? token)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
            if (token != null) request.Headers.Add("RequestVerificationToken", token);
            return await client.SendAsync(request);
        }
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/coordinator-portal/session")).StatusCode);
            var redirect = await client.GetAsync("/CoordinatorPortal/Matches");
            Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
            Assert.Contains("/CoordinatorPortal/Login", redirect.Headers.Location!.ToString());
            (await client.PostAsync("/test/default-cookie", null)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/coordinator-portal/session")).StatusCode);
            var anonymousToken = await Token("/CoordinatorPortal/Login");
            var login = new { account = "coordinator@example.test", password = "Test-only-password", rememberMe = false };
            Assert.Equal(HttpStatusCode.BadRequest, (await Write(HttpMethod.Post, "/api/coordinator-portal/login", login, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Write(HttpMethod.Post, "/api/coordinator-portal/login",
                new { account = login.account, password = "wrong" }, anonymousToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(HttpMethod.Post, "/api/coordinator-portal/login",
                new { account = "unassigned@example.test", password = login.password }, anonymousToken)).StatusCode);
            var signedIn = await Write(HttpMethod.Post, "/api/coordinator-portal/login", login, anonymousToken);
            signedIn.EnsureSuccessStatusCode();
            Assert.Contains(signedIn.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("Hanaka.Coordinator="));
            var token = await Token("/CoordinatorPortal/Matches");
            var session = await client.GetFromJsonAsync<JsonElement>("/api/coordinator-portal/session");
            Assert.Equal(2, session.GetProperty("tournaments").GetArrayLength());
            var path = $"/api/coordinator-portal/matches/{matchId}";
            var payload = new { expectedVersion = 1, courtText = "Sân 2", preparing = true, scoreTeam1 = 99, isCompleted = true };
            Assert.Equal(HttpStatusCode.BadRequest, (await Write(HttpMethod.Put, path, payload, null)).StatusCode);
            var saved = await Write(HttpMethod.Put, path, payload, token); saved.EnsureSuccessStatusCode();
            var snapshot = await saved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("PREPARING", snapshot.GetProperty("matchStatus").GetString());
            Assert.Equal(0, snapshot.GetProperty("scoreTeam1").GetInt32());
            Assert.False(snapshot.GetProperty("isCompleted").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, (await Write(HttpMethod.Put, path, payload, token)).StatusCode);
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PickleballDbContext>();
                var match = await db.TournamentGroupMatches.SingleAsync(); match.ScoreTeam1 = 1; await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Conflict, (await Write(HttpMethod.Put, path,
                new { expectedVersion = 3, courtText = "Sân 3", preparing = false }, token)).StatusCode);
            await using (var scope = app.Services.CreateAsyncScope())
                await new AdminTournamentCoordinatorsController(scope.ServiceProvider.GetRequiredService<PickleballDbContext>())
                    .Revoke(tournamentId, userId, default);
            session = await client.GetFromJsonAsync<JsonElement>("/api/coordinator-portal/session");
            Assert.Equal(otherTournamentId, Assert.Single(session.GetProperty("tournaments").EnumerateArray()).GetProperty("tournamentId").GetInt64());
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(HttpMethod.Put, path, payload, token)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Write(HttpMethod.Post, "/api/coordinator-portal/logout", new { }, null)).StatusCode);
            (await Write(HttpMethod.Post, "/api/coordinator-portal/logout", new { }, token)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/coordinator-portal/session")).StatusCode);
            anonymousToken = await Token("/CoordinatorPortal/Login");
            (await Write(HttpMethod.Post, "/api/coordinator-portal/login", new { account = "0900000000", password = login.password }, anonymousToken)).EnsureSuccessStatusCode();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PickleballDbContext>();
                (await db.Users.SingleAsync(u => u.UserId == userId)).IsActive = false; await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/coordinator-portal/session")).StatusCode);
        }
        finally { await app.StopAsync(); }
    }
}
