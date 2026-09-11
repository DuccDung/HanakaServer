using System.Data.Common;
using HanakaServer.Data;
using HanakaServer.Middleware;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace HanakaServer.Tests;

public sealed class AuthSqlFactAttribute : FactAttribute
{
    public AuthSqlFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("HANAKA_AUTH_SQL_TESTS") != "1")
            Skip = "Opt in with HANAKA_AUTH_SQL_TESTS=1 on Windows. Uses a disposable SQL LocalDB database only.";
    }
}

public sealed class AuthRequestCancellationSqlTests(ITestOutputHelper output)
{
    [AuthSqlFact]
    public async Task Actual_http_disconnect_cancels_sql_and_next_session_request_succeeds()
    {
        await using var sandbox = await AuthSqlSandbox.CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<(int Status, bool Aborted)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (Exception error) { completed.TrySetException(error); throw; }
            finally
            {
                if (context.Request.Query.ContainsKey("slow"))
                    completed.TrySetResult((context.Response.StatusCode, context.RequestAborted.IsCancellationRequested));
            }
        });
        app.UseMiddleware<RequestCancellationMiddleware>();
        app.MapGet("/api/web-auth/me", async (HttpContext context) =>
        {
            var interceptor = context.Request.Query.ContainsKey("slow")
                ? new RatingReadInterceptor(() => { }, slow: true, started) : null;
            await using var db = sandbox.CreateDb(interceptor);
            var user = await Service(db).GetAuthUserAsync(sandbox.UserId, context.RequestAborted);
            return Results.Ok(new { isAuthenticated = true, user });
        });
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var abort = new CancellationTokenSource();
            var request = client.GetAsync("/api/web-auth/me?slow=1", abort.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(100);
            abort.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            var outcome = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(outcome.Aborted);
            Assert.Equal(499, outcome.Status);

            using var response = await client.GetAsync("/api/web-auth/me");
            response.EnsureSuccessStatusCode();
            using var payload = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(payload.RootElement.GetProperty("isAuthenticated").GetBoolean());
            Assert.Equal(3.25m, payload.RootElement.GetProperty("user").GetProperty("ratingSingle").GetDecimal());
        }
        finally { await app.StopAsync(); }
    }

    [AuthSqlFact]
    public async Task Auth_rating_reads_survive_cancelled_requests_and_preserve_real_sql_errors()
    {
        await using var sandbox = await AuthSqlSandbox.CreateAsync();
        await using (var db = sandbox.CreateDb())
        {
            var user = await Service(db).GetAuthUserAsync(sandbox.UserId);
            Assert.NotNull(user);
            Assert.Equal(3.25m, user.RatingSingle);
            Assert.Equal(3.75m, user.RatingDouble);
        }

        // Exercise the real EF SQL execution strategy wrapping the captured 3980/0 errors.
        using (var abort = new CancellationTokenSource())
        {
            var interceptor = new RatingReadInterceptor(() =>
            {
                abort.Cancel();
                throw SqlExceptionTestFactory.CancelledBatch();
            });
            await using var db = sandbox.CreateDb(interceptor);
            Exception? observed = null;
            var request = new DefaultHttpContext { RequestAborted = abort.Token };
            var middleware = new RequestCancellationMiddleware(async _ =>
            {
                try { await Service(db).GetAuthUserAsync(sandbox.UserId, abort.Token); }
                catch (Exception error) { observed = error; throw; }
            }, NullLogger<RequestCancellationMiddleware>.Instance);

            await middleware.InvokeAsync(request);

            var wrapped = Assert.IsType<InvalidOperationException>(observed);
            Assert.Equal(3980, Assert.IsType<SqlException>(wrapped.InnerException).Number);
            Assert.Equal(499, request.Response.StatusCode);
            Assert.Equal(1, interceptor.RatingReads);
        }

        // Cancel an actual in-flight SQL command, without manufacturing its exception.
        using (var abort = new CancellationTokenSource())
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var interceptor = new RatingReadInterceptor(() => { }, slow: true, started);
            await using var db = sandbox.CreateDb(interceptor);
            var request = new DefaultHttpContext { RequestAborted = abort.Token };
            var middleware = new RequestCancellationMiddleware(
                async _ => { await Service(db).GetAuthUserAsync(sandbox.UserId, abort.Token); },
                NullLogger<RequestCancellationMiddleware>.Instance);

            var processing = middleware.InvokeAsync(request);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(100);
            abort.Cancel();
            try { await processing.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (SqlException error)
            {
                foreach (SqlError detail in error.Errors)
                    output.WriteLine($"SQL {detail.Number}, class {detail.Class}, state {detail.State}: {detail.Message}");
                throw;
            }

            Assert.Equal(499, request.Response.StatusCode);
            Assert.Equal(1, interceptor.RatingReads);
        }

        // A real schema error must escape the middleware even if the request later aborts.
        using (var abort = new CancellationTokenSource())
        {
            var interceptor = new RatingReadInterceptor(() => { }, missingTable: true);
            await using var db = sandbox.CreateDb(interceptor);
            var request = new DefaultHttpContext { RequestAborted = abort.Token };
            var middleware = new RequestCancellationMiddleware(async _ =>
            {
                try { await Service(db).GetAuthUserAsync(sandbox.UserId, abort.Token); }
                catch { abort.Cancel(); throw; }
            }, NullLogger<RequestCancellationMiddleware>.Instance);

            var error = await Assert.ThrowsAsync<SqlException>(() => middleware.InvokeAsync(request));
            Assert.Equal(208, error.Number);
        }

        // Subsequent requests, including concurrent tabs, still return the latest rating.
        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var db = sandbox.CreateDb();
            var user = await Service(db).GetAuthUserAsync(sandbox.UserId);
            Assert.NotNull(user);
            Assert.Equal(3.25m, user.RatingSingle);
            Assert.Equal(3.75m, user.RatingDouble);
        }));
    }

    private static AppAuthService Service(PickleballDbContext db) => new(
        db, new ConfigurationBuilder().Build(), null!, null!, new HttpContextAccessor());

    private sealed class RatingReadInterceptor(Action onRead, bool slow = false,
        TaskCompletionSource? started = null, bool missingTable = false) : DbCommandInterceptor
    {
        public int RatingReads { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[UserRatingHistory]", StringComparison.Ordinal))
            {
                RatingReads++;
                onRead();
                if (missingTable) command.CommandText = "SELECT * FROM dbo.DeliberatelyMissingAuthTestTable";
                if (slow) command.CommandText = "WAITFOR DELAY '00:00:05'; " + command.CommandText;
                started?.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AuthSqlSandbox : IAsyncDisposable
    {
        private const string Master = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;Pooling=false";
        private readonly string name = "HanakaAuthCancellationTests_" + Guid.NewGuid().ToString("N");
        public long UserId { get; private set; }

        public PickleballDbContext CreateDb(DbCommandInterceptor? interceptor = null)
        {
            var connection = new SqlConnectionStringBuilder(Master) { InitialCatalog = name }.ConnectionString;
            var options = new DbContextOptionsBuilder<PickleballDbContext>().UseSqlServer(connection);
            if (interceptor != null) options.AddInterceptors(interceptor);
            return new PickleballDbContext(options.Options);
        }

        public static async Task<AuthSqlSandbox> CreateAsync()
        {
            var sandbox = new AuthSqlSandbox();
            await ExecuteMasterAsync($"CREATE DATABASE [{sandbox.name}]");
            try
            {
                await using var db = sandbox.CreateDb();
                await db.Database.EnsureCreatedAsync();
                var user = new User { FullName = "Auth cancellation test", IsActive = true, CreatedAt = DateTime.UtcNow };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                sandbox.UserId = user.UserId;
                var ratedAt = DateTime.UtcNow;
                db.UserRatingHistories.Add(new UserRatingHistory { UserId = user.UserId, RatedAt = ratedAt, RatingSingle = 2, RatingDouble = 2 });
                await db.SaveChangesAsync();
                db.UserRatingHistories.Add(new UserRatingHistory { UserId = user.UserId, RatedAt = ratedAt, RatingSingle = 3.25m, RatingDouble = 3.75m });
                await db.SaveChangesAsync();
                return sandbox;
            }
            catch { await sandbox.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync() => await ExecuteMasterAsync($"DROP DATABASE [{name}]");

        private static async Task ExecuteMasterAsync(string text)
        {
            await using var connection = new SqlConnection(Master);
            await connection.OpenAsync();
            await using var command = new SqlCommand(text, connection) { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync();
        }
    }
}
