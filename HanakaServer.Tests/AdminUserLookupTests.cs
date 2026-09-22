using System.Text.Json;
using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HanakaServer.Tests;

public sealed class AdminUserLookupTests
{
    [Theory]
    [InlineData("42", "0912345678")]
    [InlineData("0912345678", "+84 (912) 345-678")]
    [InlineData("+84 912.345.678", "0912345678")]
    [InlineData("84912345678", " 0912.345.678 ")]
    [InlineData(" (0912) 345-678 ", "84912345678")]
    public async Task Exact_id_or_equivalent_phone_returns_active_user_and_latest_ratings(string query, string phone)
    {
        await using var db = NewDb();
        await SeedUser(db, 42, phone);
        db.UserRatingHistories.AddRange(
            new UserRatingHistory { UserId = 42, RatedAt = new DateTime(2026, 1, 1), RatingSingle = 2, RatingDouble = 2.5m },
            new UserRatingHistory { UserId = 42, RatedAt = new DateTime(2026, 1, 2), RatingSingle = 3, RatingDouble = 3.5m });
        await db.SaveChangesAsync();
        var user = Assert.Single(Items(await new AdminUsersController(db).Lookup(query, default)));
        Assert.Equal(42, user.GetProperty("userId").GetInt64());
        Assert.Equal(3.5m, user.GetProperty("ratingDouble").GetDecimal());
        Assert.Equal("Test user", user.GetProperty("fullName").GetString());
        Assert.False(user.TryGetProperty("passwordHash", out _));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("+", false)]
    [InlineData("abc0912345678", false)]
    [InlineData("84+912345678", false)]
    [InlineData("++84912345678", false)]
    [InlineData("999999999999999999999999", false)]
    [InlineData("09123", true)]
    [InlineData("999", true)]
    public async Task Invalid_or_partial_identifiers_never_choose_an_unrelated_user(string query, bool valid)
    {
        await using var db = NewDb();
        await SeedUser(db, 42, "0912345678");
        var result = await new AdminUsersController(db).Lookup(query, default);
        if(valid) Assert.Empty(Items(result));
        else Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Inactive_accounts_are_excluded_and_ambiguity_is_returned_for_explicit_selection()
    {
        await using var db = NewDb();
        await SeedUser(db, 42, "0912345678");
        await SeedUser(db, 43, "+84912345678");
        await SeedUser(db, 44, "0912345678", active: false);
        await SeedUser(db, 912345678, null); // The digits could also be a real User ID.
        var controller = new AdminUsersController(db);
        Assert.Equal(new long[] { 42, 43, 912345678 }, Items(await controller.Lookup("0912345678", default))
            .Select(x => x.GetProperty("userId").GetInt64()));
        Assert.Empty(Items(await controller.Lookup("44", default)));
        Assert.Equal(2, Items(await controller.Lookup("+84 912 345 678", default)).Length);
    }

    [Fact]
    public async Task An_excessive_number_of_phone_matches_requires_a_more_specific_lookup()
    {
        await using var db = NewDb();
        for(var id = 1; id <= 21; id++) await SeedUser(db, id, "0912345678");
        Assert.IsType<ConflictObjectResult>(await new AdminUsersController(db).Lookup("0912345678", default));
    }

    [AuthSqlFact]
    public async Task Lookup_executes_phone_normalization_and_rating_projection_on_real_sql_server()
    {
        const string master = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;Pooling=false";
        var databaseName = "HanakaUserLookupTests_" + Guid.NewGuid().ToString("N");
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using(var create = new SqlCommand($"CREATE DATABASE [{databaseName}]", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var connectionString = new SqlConnectionStringBuilder(master) { InitialCatalog = databaseName }.ConnectionString;
            await using var db = new PickleballDbContext(new DbContextOptionsBuilder<PickleballDbContext>().UseSqlServer(connectionString).Options);
            await db.Database.EnsureCreatedAsync();
            var user = new User { FullName = "SQL lookup test", Phone = "+84 (912) 345-678", IsActive = true, CreatedAt = DateTime.UtcNow, RatingDouble = 2 };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserRatingHistories.Add(new UserRatingHistory { UserId = user.UserId, RatedAt = DateTime.UtcNow, RatingDouble = 3.75m });
            await db.SaveChangesAsync();
            var controller = new AdminUsersController(db);
            Assert.Equal(3.75m, Assert.Single(Items(await controller.Lookup("0912345678", default))).GetProperty("ratingDouble").GetDecimal());
            // Empty phone candidates must also translate correctly for short IDs.
            Assert.Equal(user.UserId, Assert.Single(Items(await controller.Lookup(user.UserId.ToString(), default))).GetProperty("userId").GetInt64());
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Lookup("0912345678", cancelled.Token));
        }
        finally
        {
            await using var drop = new SqlCommand($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static PickleballDbContext NewDb() => new(new DbContextOptionsBuilder<PickleballDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SeedUser(PickleballDbContext db, long id, string? phone, bool active = true)
    {
        db.Users.Add(new User { UserId = id, Phone = phone, FullName = "Test user", IsActive = active, CreatedAt = DateTime.UtcNow, RatingDouble = 2 });
        await db.SaveChangesAsync();
    }

    private static JsonElement[] Items(IActionResult result) => JsonSerializer.SerializeToElement(
        Assert.IsType<OkObjectResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        .GetProperty("items").EnumerateArray().ToArray();
}
