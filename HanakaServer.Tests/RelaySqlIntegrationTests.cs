using System.Text.RegularExpressions;
using HanakaServer.Data;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Dtos.Relay;
using HanakaServer.Models;
using HanakaServer.Services.Brackets;
using HanakaServer.Services.Relay;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public sealed class RelaySqlFactAttribute : FactAttribute
{
    public RelaySqlFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("HANAKA_RELAY_SQL_TESTS") != "1")
            Skip = "Opt in with HANAKA_RELAY_SQL_TESTS=1 on Windows with SQL LocalDB. Uses isolated disposable databases only.";
    }
}

public sealed class RelaySqlIntegrationTests
{
    [RelaySqlFact]
    public async Task Bracket_participant_mode_migration_adds_constraint_classifies_known_draft_and_is_repeatable()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        await using (var db = sandbox.CreateDb())
        {
            var templates = new BracketTemplateService(db, new BracketTemplateValidationService(),
                NullLogger<BracketTemplateService>.Instance);
            var created = await templates.CreateAsync(new CreateBracketTemplateRequest
            {
                TemplateCode = "TP_08",
                TemplateName = "Giải tiếp sức",
                ParticipantMode = BracketTemplateParticipantModes.Standard,
                FormatType = BracketTemplateFormatTypes.Custom
            }, null, default);
            Assert.True(created.Success, created.Message);
        }

        await sandbox.SqlAsync("""
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.BracketTemplates')
                       AND name = N'IX_BracketTemplates_Status_ParticipantMode_FormatType')
                DROP INDEX IX_BracketTemplates_Status_ParticipantMode_FormatType ON dbo.BracketTemplates;
            IF OBJECT_ID(N'dbo.CK_BracketTemplates_ParticipantMode', N'C') IS NOT NULL
                ALTER TABLE dbo.BracketTemplates DROP CONSTRAINT CK_BracketTemplates_ParticipantMode;
            DECLARE @defaultName sysname;
            SELECT @defaultName = dc.name
            FROM sys.default_constraints dc
            JOIN sys.columns c ON c.default_object_id = dc.object_id
            WHERE dc.parent_object_id = OBJECT_ID(N'dbo.BracketTemplates') AND c.name = N'ParticipantMode';
            IF @defaultName IS NOT NULL
                EXEC(N'ALTER TABLE dbo.BracketTemplates DROP CONSTRAINT [' + @defaultName + N']');
            ALTER TABLE dbo.BracketTemplates DROP COLUMN ParticipantMode;
            """);

        await sandbox.MigrateParticipantModeAsync();
        await sandbox.MigrateParticipantModeAsync();
        await using var reloaded = sandbox.CreateDb();
        Assert.Equal(BracketTemplateParticipantModes.RelayTeam,
            await reloaded.BracketTemplates.Where(x => x.TemplateCode == "TP_08")
                .Select(x => x.ParticipantMode).SingleAsync());
        await Assert.ThrowsAsync<SqlException>(() => sandbox.SqlAsync(
            "UPDATE dbo.BracketTemplates SET ParticipantMode = 'INVALID' WHERE TemplateCode = 'TP_08'"));
    }

    [RelaySqlFact]
    public async Task Full_EF_schema_accepts_additive_migration_and_preserves_legacy_registration_fields()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        await using (var db = sandbox.CreateDb())
        {
            var tournament = new Tournament
            {
                Title = "Giải đôi giữ nguyên", Status = "DRAFT", GameType = "DOUBLE", GenderCategory = "OPEN", ExpectedTeams = 8
            };
            tournament.TournamentRegistrations.Add(new TournamentRegistration
            {
                RegCode = "LEGACY-1", RegIndex = 1, Player1Name = "An", Player2Name = "Bình", Success = true
            });
            db.Tournaments.Add(tournament);
            await db.SaveChangesAsync();
        }
        await sandbox.MigrateAsync();
        await sandbox.MigrateBracketAsync();
        await using var reloaded = sandbox.CreateDb();
        var registration = await reloaded.TournamentRegistrations.SingleAsync();
        Assert.Equal("An", registration.Player1Name);
        Assert.Equal("Bình", registration.Player2Name);
        Assert.Equal("DOUBLE", await reloaded.Tournaments.Select(x => x.GameType).SingleAsync());
        Assert.Empty(await reloaded.RelayTournamentSettings.ToListAsync());
    }

    [RelaySqlFact]
    public async Task Reader_recovers_consistent_score_version_and_deadline_after_new_connection()
    {
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await sandbox.SeedTeamsAsync();
        var clock = new RelayTestClock();
        await sandbox.ExecuteAsync(new(Guid.NewGuid(), 0, "START"), clock);
        await sandbox.ExecuteAsync(new(Guid.NewGuid(), 1, "AWARD_POINT", 2), clock);
        clock.Now = clock.Now.AddMinutes(7);
        await using var db = sandbox.CreateDb();
        var result = await new RelayMatchReader(db, clock).ReadAsync(100);
        Assert.NotNull(result);
        Assert.Equal(1, result.TournamentId);
        Assert.Equal(1, result.Snapshot.ScoreTeam2);
        Assert.Equal(2, result.Snapshot.Version);
        Assert.Equal(180, result.Snapshot.RemainingSeconds(result.ServerNowUtc));
        Assert.Null(await new RelayMatchReader(db, clock).ReadAsync(999));
    }

    [RelaySqlFact]
    public async Task Admin_preparation_saves_versioned_settings_without_enabling_competition()
    {
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await using (var db = sandbox.CreateDb())
            Assert.Equal("TOURNAMENT_HAS_MATCHES", (await Assert.ThrowsAsync<RelayRuleException>(() =>
                new RelayAdminService(db).SaveSettingsAsync(1, new() { TeamSize = 6 }, default))).Code);
        await sandbox.SqlAsync("DELETE dbo.TournamentGroupMatches WHERE MatchId=100");
        await using (var db = sandbox.CreateDb())
        {
            var settings = await new RelayAdminService(db).SaveSettingsAsync(1, new() { TeamSize = 6 }, default);
            Assert.False(settings.IsEnabled);
            Assert.Equal(3, settings.PairCount);
            Assert.Equal(40, settings.TargetScore);
            Assert.Equal(1, settings.Version);
        }
        await using (var db = sandbox.CreateDb())
            Assert.Equal("VERSION_CONFLICT", (await Assert.ThrowsAsync<RelayRuleException>(() =>
                new RelayAdminService(db).SaveSettingsAsync(1, new() { ExpectedVersion = 0 }, default))).Code);
        await using (var db = sandbox.CreateDb())
        {
            var settings = await new RelayAdminService(db).SaveSettingsAsync(1,
                new() { ExpectedVersion = 1, TeamSize = 6, TargetScore = 55 }, default);
            Assert.Equal(2, settings.Version);
            Assert.Equal(55, settings.TargetScore);
            Assert.False(settings.IsEnabled);
            await new RelayLineupService(db, new RelayMatchLineupSnapshotService(db, new RelayTestClock())).SaveDraftAsync(10, 1, "Đội A", 1,
                [new(1, 1, "VĐV 1")], 0, default);
        }
        await using (var db = sandbox.CreateDb())
            Assert.Equal("TEAM_SIZE_LOCKED", (await Assert.ThrowsAsync<RelayRuleException>(() =>
                new RelayAdminService(db).SaveSettingsAsync(1, new() { ExpectedVersion = 2, TeamSize = 8 }, default))).Code);
    }

    [RelaySqlFact]
    public async Task Admin_can_activate_complete_teams_with_a_configurable_target()
    {
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await sandbox.SqlAsync("DELETE dbo.TournamentGroupMatches WHERE MatchId=100");
        await using var db = sandbox.CreateDb();
        var admin = new RelayAdminService(db);
        var settings = await admin.SaveSettingsAsync(1, new() { TeamSize = 6, TargetScore = 35 }, default);
        var lineups = new RelayLineupService(db, new RelayMatchLineupSnapshotService(db, new RelayTestClock()));
        foreach (var (registrationId, offset) in new[] { (10L, 0), (20L, 6) })
        {
            var members = Enumerable.Range(1, 6)
                .Select(position => new RelayMemberInput(position, position + offset, $"VĐV {position + offset}"))
                .ToList();
            await lineups.SaveDraftAsync(registrationId, 1, $"Đội {registrationId}", offset + 1,
                members, 0, default);
        }

        var active = await admin.ActivateAsync(1, new() { ExpectedVersion = settings.Version }, default);
        Assert.True(active.IsEnabled);
        Assert.Equal(35, active.TargetScore);
        Assert.Equal(3, active.PairCount);
        await sandbox.SqlAsync("""
            INSERT dbo.TournamentGroupMatches
                (MatchId,TournamentId,Team1RegistrationId,Team2RegistrationId,RefereeUserId)
            VALUES (100,1,10,20,1)
            """);
        var started = await sandbox.ExecuteAsync(new(Guid.NewGuid(), 0, "START"), new RelayTestClock());
        Assert.Equal(35, started.Snapshot.Rules.TargetScore);
    }

    [RelaySqlFact]
    public async Task Migration_is_repeatable_and_does_not_enable_or_rewrite_existing_tournaments()
    {
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await sandbox.MigrateAsync(); // second application
        await using var db = sandbox.CreateDb();
        Assert.Equal(0, await db.RelayTournamentSettings.CountAsync());
        Assert.Equal(2, await db.Tournaments.CountAsync());
        Assert.Equal(2, await db.TournamentRegistrations.CountAsync());
        await sandbox.SqlAsync("""
            INSERT dbo.RelayTournamentSettings VALUES (1,6,40,600,NULL,1,0);
            """);
        Assert.True(await db.RelayTournamentSettings.Where(x => x.TournamentId == 1).Select(x => x.IsEnabled).SingleAsync());
    }

    [RelaySqlFact]
    public async Task Roster_is_editable_but_team_size_and_tournament_ownership_remain_protected()
    {
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await sandbox.SeedTeamsAsync();
        await sandbox.SqlAsync("UPDATE dbo.RelayTeamMembers SET DisplayName=N'Đổi người' WHERE RegistrationId=10 AND Position=1");
        await sandbox.SqlAsync("UPDATE dbo.RelayTeams SET LineupLockedAtUtc=NULL WHERE RegistrationId=10");
        await Assert.ThrowsAsync<SqlException>(() => sandbox.SqlAsync("UPDATE dbo.RelayTournamentSettings SET TeamSize=8 WHERE TournamentId=1"));
        await sandbox.SqlAsync("INSERT dbo.RelayTournamentSettings VALUES (2,6,40,600,NULL,0,0)");
        await Assert.ThrowsAsync<SqlException>(() => sandbox.SqlAsync("UPDATE dbo.RelayTeams SET TournamentId=2 WHERE RegistrationId=10"));
        await using var db = sandbox.CreateDb();
        Assert.Equal(6, await db.RelayTeamMembers.CountAsync(x => x.RegistrationId == 10));
        Assert.Equal("Đổi người", (await db.RelayTeamMembers.SingleAsync(x => x.RegistrationId == 10 && x.Position == 1)).DisplayName);
    }

    [RelaySqlFact]
    public async Task Draft_positions_can_be_reordered_and_legacy_lock_timestamp_does_not_freeze_roster()
    {
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await sandbox.ConfigureAsync();
        var clock = new RelayTestClock();
        await using (var db = sandbox.CreateDb())
        {
            var service = new RelayLineupService(db, new RelayMatchLineupSnapshotService(db, clock));
            await service.SaveDraftAsync(10, 1, "Đội A", 1, [new(1, 1, "Một"), new(2, 2, "Hai")], 0, default);
        }
        await using (var db = sandbox.CreateDb())
        {
            var service = new RelayLineupService(db, new RelayMatchLineupSnapshotService(db, clock));
            var swapped = await service.SaveDraftAsync(10, 1, "Đội A", 1, [new(1, 2, "Hai"), new(2, 1, "Một")], 1, default);
            Assert.Equal(2, swapped.Members.Single(x => x.Position == 1).UserId);
        }
        await sandbox.SqlAsync("UPDATE dbo.RelayTeams SET LineupLockedAtUtc=SYSDATETIMEOFFSET() WHERE RegistrationId=10");
        await sandbox.SqlAsync("UPDATE dbo.RelayTeamMembers SET DisplayName=N'Vẫn sửa được' WHERE RegistrationId=10 AND Position=1");
        await sandbox.SqlAsync("INSERT dbo.RelayTournamentSettings VALUES (2,6,40,600,NULL,0,0)");
        await Assert.ThrowsAsync<SqlException>(() => sandbox.SqlAsync("UPDATE dbo.RelayTeams SET TournamentId=2 WHERE RegistrationId=10"));
    }

    [RelaySqlFact]
    public async Task Retried_commands_replay_exact_result_and_modified_request_id_is_rejected()
    {
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await sandbox.SeedTeamsAsync();
        var clock = new RelayTestClock();
        var command = new RelayCommandRequest(Guid.NewGuid(), 0, "START");
        var started = await sandbox.ExecuteAsync(command, clock);
        var replay = await sandbox.ExecuteAsync(command, clock);
        Assert.True(replay.Replayed);
        Assert.Equal(started.Snapshot.Version, replay.Snapshot.Version);
        Assert.Equal(started.Snapshot.LegEndsAtUtc, replay.Snapshot.LegEndsAtUtc);
        Assert.Equal("REQUEST_ID_REUSED", (await Assert.ThrowsAsync<RelayRuleException>(() =>
            sandbox.ExecuteAsync(command with { Operation = "NEXT_LEG" }, clock))).Code);
        await using var db = sandbox.CreateDb();
        Assert.Equal(1, await db.RelayLegs.CountAsync());
        Assert.Equal(1, await db.RelayMatchCommands.CountAsync());
    }

    [RelaySqlFact]
    public async Task Concurrent_score_writes_accept_one_version_and_retry_cannot_duplicate_point()
    {
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await sandbox.SeedTeamsAsync();
        var clock = new RelayTestClock();
        await sandbox.ExecuteAsync(new(Guid.NewGuid(), 0, "START"), clock);
        var request = new RelayCommandRequest(Guid.NewGuid(), 1, "AWARD_POINT", 1);
        var sameRequestResults = await Task.WhenAll(sandbox.ExecuteAsync(request, clock), sandbox.ExecuteAsync(request, clock));
        Assert.Single(sameRequestResults, x => x.Replayed);
        var errors = await Task.WhenAll(
            Record.ExceptionAsync(() => sandbox.ExecuteAsync(new(Guid.NewGuid(), 2, "AWARD_POINT", 1), clock)),
            Record.ExceptionAsync(() => sandbox.ExecuteAsync(new(Guid.NewGuid(), 2, "AWARD_POINT", 2), clock)));
        Assert.Single(errors, x => x == null);
        Assert.Equal("VERSION_CONFLICT", Assert.IsType<RelayRuleException>(errors.Single(x => x != null)).Code);
        await using var db = sandbox.CreateDb();
        var scores = await db.TournamentGroupMatches.Select(x => new { x.ScoreTeam1, x.ScoreTeam2 }).SingleAsync();
        Assert.Equal(2, scores.ScoreTeam1 + scores.ScoreTeam2);
        Assert.Equal(2, await db.TournamentMatchScoreHistories.CountAsync());
        Assert.Equal(3, await db.RelayMatchCommands.CountAsync());
    }

    [RelaySqlFact]
    public async Task Informational_clock_allows_late_points_and_transaction_failure_keeps_previous_score()
    {
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await sandbox.SeedTeamsAsync();
        var clock = new RelayTestClock();
        var state = (await sandbox.ExecuteAsync(new(Guid.NewGuid(), 0, "START"), clock)).Snapshot;
        clock.Now = clock.Now.AddMinutes(11);
        state = (await sandbox.ExecuteAsync(new(Guid.NewGuid(), state.Version, "AWARD_POINT", 1), clock)).Snapshot;
        state = (await sandbox.ExecuteAsync(new(Guid.NewGuid(), state.Version, "FINISH_LEG"), clock)).Snapshot;
        Assert.Equal(1, state.ScoreTeam1);
        clock.Now = clock.Now.AddMinutes(1);
        state = (await sandbox.ExecuteAsync(new(Guid.NewGuid(), state.Version, "NEXT_LEG"), clock)).Snapshot;
        Assert.Equal(2, state.PairNumber);
        Assert.Equal(600, state.RemainingSeconds(clock.Now));
        // Fail the last write of a command: the score UPDATE and history must both roll back.
        await sandbox.SqlAsync("ALTER TABLE dbo.RelayMatchCommands WITH NOCHECK ADD CONSTRAINT CK_Test_RejectPoint CHECK (Operation <> 'AWARD_POINT')");
        await Assert.ThrowsAsync<DbUpdateException>(() => sandbox.ExecuteAsync(new(Guid.NewGuid(), state.Version, "AWARD_POINT", 2), clock));
        await using var db = sandbox.CreateDb();
        Assert.Equal(0, await db.TournamentGroupMatches.Select(x => x.ScoreTeam2).SingleAsync());
        Assert.Equal(state.Version, await db.RelayMatchStates.Select(x => x.Version).SingleAsync());
        Assert.Equal(1, await db.TournamentMatchScoreHistories.CountAsync());
    }

    [RelaySqlFact]
    public async Task Unauthorized_actor_cannot_replay_or_change_match()
    {
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await sandbox.SeedTeamsAsync();
        await using var db = sandbox.CreateDb();
        var error = await Assert.ThrowsAsync<RelayRuleException>(() => new RelayMatchStore(db, new RelayTestClock())
            .ExecuteAsync(100, 2, false, new(Guid.NewGuid(), 0, "START")));
        Assert.Equal("FORBIDDEN", error.Code);
        Assert.Equal(0, await db.RelayMatchCommands.CountAsync());
    }

    [RelaySqlFact]
    public async Task Concurrent_winning_point_and_manual_leg_finish_accept_only_one_version()
    {
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await sandbox.SeedTeamsAsync();
        var clock = new RelayTestClock();
        var state = (await sandbox.ExecuteAsync(new(Guid.NewGuid(), 0, "START"), clock)).Snapshot;
        clock.Now = clock.Now.AddMinutes(9);
        for (var i = 0; i < 39; i++) state = (await sandbox.ExecuteAsync(new(Guid.NewGuid(), state.Version, "AWARD_POINT", 1), clock)).Snapshot;
        for (var i = 0; i < 38; i++) state = (await sandbox.ExecuteAsync(new(Guid.NewGuid(), state.Version, "AWARD_POINT", 2), clock)).Snapshot;
        var beforeDeadline = new RelayTestClock { Now = clock.Now.AddSeconds(59) };
        var atDeadline = new RelayTestClock { Now = clock.Now.AddMinutes(1) };
        var errors = await Task.WhenAll(
            Record.ExceptionAsync(() => sandbox.ExecuteAsync(new(Guid.NewGuid(), state.Version, "AWARD_POINT", 1), beforeDeadline)),
            Record.ExceptionAsync(() => sandbox.ExecuteAsync(new(Guid.NewGuid(), state.Version, "FINISH_LEG"), atDeadline)));
        Assert.Single(errors, x => x == null);
        Assert.Equal("VERSION_CONFLICT", Assert.IsType<RelayRuleException>(errors.Single(x => x != null)).Code);
        await using var db = sandbox.CreateDb();
        var recovered = (await new RelayMatchReader(db, atDeadline).ReadAsync(100))!.Snapshot;
        Assert.Contains(recovered.Status, new[] { RelayStatuses.Completed, RelayStatuses.AwaitingChange });
        Assert.Contains(recovered.ScoreTeam1, new[] { 39, 40 });
        Assert.Equal(38, recovered.ScoreTeam2);
        Assert.Equal(recovered.Status == RelayStatuses.Completed ? 10L : null, recovered.WinnerRegistrationId);
        Assert.Single(recovered.Legs);
        Assert.NotNull(recovered.Legs[0].FinishedAtUtc);
        Assert.Equal(79, await db.RelayMatchCommands.CountAsync());
        Assert.Contains(await db.TournamentMatchScoreHistories.CountAsync(), new[] { 77, 78 });
        if (recovered.Status == RelayStatuses.Completed)
        {
            Assert.Equal("MATCH_STATE_INVALID", (await Assert.ThrowsAsync<RelayRuleException>(() =>
                sandbox.ExecuteAsync(new(Guid.NewGuid(), recovered.Version, "NEXT_LEG"), atDeadline))).Code);
        }
        else
        {
            var next = await sandbox.ExecuteAsync(new(Guid.NewGuid(), recovered.Version, "NEXT_LEG"), atDeadline);
            Assert.Equal(RelayStatuses.Running, next.Snapshot.Status);
            Assert.Equal(2, next.Snapshot.PairNumber);
        }
    }
}

internal sealed class RelayTestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 6, 8, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class RelaySqlSandbox : IAsyncDisposable
{
    private const string Master = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30;Pooling=false";
    private readonly string name = "HanakaRelayTests_" + Guid.NewGuid().ToString("N");
    public string ConnectionString => new SqlConnectionStringBuilder(Master) { InitialCatalog = name }.ConnectionString;
    public PickleballDbContext CreateDb() => new(new DbContextOptionsBuilder<PickleballDbContext>().UseSqlServer(ConnectionString).Options);

    public static async Task<RelaySqlSandbox> CreateFullSchemaAsync()
    {
        var sandbox = new RelaySqlSandbox();
        await ExecuteSqlAsync(Master, $"CREATE DATABASE [{sandbox.name}]");
        try
        {
            await using var db = sandbox.CreateDb();
            await db.Database.EnsureCreatedAsync();
            // Simulate an existing installed library before the new per-application relay table.
            await sandbox.SqlAsync("DROP TABLE dbo.RelayBracketSeedSnapshots; DROP TABLE dbo.RelayTeamReserveMembers;");
            await sandbox.MigrateAsync();
            await sandbox.MigrateBracketAsync();
            await sandbox.MigrateParticipantModeAsync();
            return sandbox;
        }
        catch { await sandbox.DisposeAsync(); throw; }
    }

    public static async Task<RelaySqlSandbox> CreateAsync()
    {
        var sandbox = new RelaySqlSandbox();
        await ExecuteSqlAsync(Master, $"CREATE DATABASE [{sandbox.name}]");
        try
        {
            // Minimal legacy parent schema: no copied application data or real configured connection string.
            await sandbox.SqlAsync("""
                CREATE TABLE dbo.Tournaments (TournamentId bigint PRIMARY KEY, Remove bit NOT NULL DEFAULT 0);
                CREATE TABLE dbo.Users (UserId bigint PRIMARY KEY);
                CREATE TABLE dbo.TournamentRegistrations (RegistrationId bigint PRIMARY KEY, TournamentId bigint NOT NULL, IsVirtualTeam bit NOT NULL DEFAULT 0, Player1UserId bigint NULL, Player2UserId bigint NULL);
                CREATE TABLE dbo.TournamentGroupMatches (MatchId bigint PRIMARY KEY, TournamentId bigint NOT NULL,
                    Team1RegistrationId bigint NULL, Team2RegistrationId bigint NULL, RefereeUserId bigint NULL,
                    ScoreTeam1 int NOT NULL DEFAULT 0, ScoreTeam2 int NOT NULL DEFAULT 0, IsCompleted bit NOT NULL DEFAULT 0,
                    WinnerRegistrationId bigint NULL, CompletionReason varchar(30) NULL, UpdatedAt datetime2 NULL);
                CREATE TABLE dbo.TournamentMatchScoreHistories (ScoreHistoryId bigint IDENTITY PRIMARY KEY, MatchId bigint NOT NULL,
                    RefereeUserId bigint NOT NULL, ScoreTeam1 int NOT NULL DEFAULT 0, ScoreTeam2 int NOT NULL DEFAULT 0, IsCompleted bit NOT NULL DEFAULT 0,
                    WinnerRegistrationId bigint NULL, Note nvarchar(max) NULL, CreatedAt datetime2 NOT NULL DEFAULT SYSDATETIME());
                INSERT dbo.Tournaments (TournamentId) VALUES (1),(2);
                INSERT dbo.TournamentRegistrations (RegistrationId,TournamentId) VALUES (10,1),(20,1);
                INSERT dbo.TournamentGroupMatches (MatchId,TournamentId,Team1RegistrationId,Team2RegistrationId,RefereeUserId) VALUES (100,1,10,20,1);
                INSERT dbo.Users VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12);
                """);
            await sandbox.MigrateAsync();
            return sandbox;
        }
        catch { await sandbox.DisposeAsync(); throw; }
    }

    public async Task MigrateAsync()
    {
        await SqlAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", "20260906_add_relay_foundation.sql")));
        await SqlAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", "20260906_relay_informational_timer_configurable_target.sql")));
        await SqlAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", "20260908_remove_relay_lineup_lock_and_add_match_snapshots.sql")));
        await MigrateReservesAsync();
    }
    public Task MigrateReservesAsync() => SqlAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", "20260910_add_relay_reserve_members.sql")));
    public Task MigrateBracketAsync() => SqlAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", "20260906_add_relay_bracket_snapshots.sql")));
    public Task MigrateParticipantModeAsync() => SqlAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", "20260906_add_bracket_template_participant_mode.sql")));
    public Task ConfigureAsync() => SqlAsync("INSERT dbo.RelayTournamentSettings VALUES (1,6,40,600,NULL,1,0)");
    public async Task SeedTeamsAsync()
    {
        await ConfigureAsync();
        foreach (var (id, offset) in new[] { (10L, 0), (20L, 6) })
        {
            await using var db = CreateDb();
            var service = new RelayLineupService(db, new RelayMatchLineupSnapshotService(db, new RelayTestClock()));
            var members = Enumerable.Range(1, 6).Select(x => new RelayMemberInput(x, x + offset, $"VĐV {x + offset}")).ToList();
            await service.SaveDraftAsync(id, 1, $"Đội {id}", offset + 1, members, 0, default);
        }
    }

    public async Task<RelayCommandResult> ExecuteAsync(RelayCommandRequest request, TimeProvider clock)
    {
        await using var db = CreateDb();
        return await new RelayMatchStore(db, clock).ExecuteAsync(100, 1, false, request);
    }

    public Task SqlAsync(string sql) => ExecuteSqlAsync(ConnectionString, sql);
    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Only this randomly named test database may be removed. Never use application configuration here.
        if (!Regex.IsMatch(name, "^HanakaRelayTests_[a-f0-9]{32}$")) throw new InvalidOperationException("Unsafe test database name.");
        await ExecuteSqlAsync(Master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
    }
}
