using System.Text.Json;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Models;
using HanakaServer.Options;
using HanakaServer.Services;
using HanakaServer.Services.Brackets;
using HanakaServer.Services.Relay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public sealed partial class BracketTemplateWorkflowIntegrationTests
{
    [Fact]
    public async Task Relay_template_only_applies_to_relay_tournaments_and_keeps_instances_independent()
    {
        await using var db = CreateDb();
        var templates = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(templates, "SHARED-RELAY-8",
            BracketTemplateFormatTypes.SingleElimination, 8, 8, false,
            BracketTemplateParticipantModes.RelayTeam);
        var graph = BracketTemplateService.GenerateSingleElimination(8, false, [0]);
        graph.RowVersion = rowVersion;
        Assert.True((await templates.SaveGraphAsync(versionId, graph, default)).Success);
        Assert.True((await templates.PublishAsync(versionId, null, default)).Success);
        var templateBefore = JsonSerializer.Serialize(await templates.GetGraphAsync(versionId, default));
        var doubleId = await SeedTournamentAsync(db, 8);
        var tournamentIds = new List<long> { doubleId };
        foreach (var suffix in new[] { "A", "B" })
        {
            var tournament = new Tournament
            {
                Title = $"Relay {suffix}", Status = "DRAFT", GameType = "DOUBLE", GenderCategory = "OPEN",
                ExpectedTeams = 8, CreatedAt = DateTime.UtcNow, RegistrationLockedAt = DateTime.UtcNow
            };
            for (var number = 1; number <= 8; number++)
                tournament.TournamentRegistrations.Add(new TournamentRegistration
                {
                    RegCode = $"{suffix}-{number}", RegIndex = number, Success = true, Paid = true,
                    Player1Name = $"{suffix}{number}-VĐV1", Player2Name = $"{suffix}{number}-VĐV2", CreatedAt = DateTime.UtcNow
                });
            db.Tournaments.Add(tournament);
            await db.SaveChangesAsync();
            tournamentIds.Add(tournament.TournamentId);
            db.RelayTournamentSettings.Add(new() { TournamentId = tournament.TournamentId, TeamSize = 6 });
            foreach (var registration in tournament.TournamentRegistrations)
            {
                var team = new RelayTeam
                {
                    RegistrationId = registration.RegistrationId, TournamentId = tournament.TournamentId,
                    TeamName = $"Đội {registration.RegCode}", LineupLockedAtUtc = DateTimeOffset.UtcNow
                };
                foreach (var position in Enumerable.Range(1, 6))
                    team.Members.Add(new() { RegistrationId = team.RegistrationId, Position = position, DisplayName = $"{registration.RegCode}-VĐV{position}" });
                db.RelayTeams.Add(team);
            }
            await db.SaveChangesAsync();
        }
        var applications = new TournamentBracketApplicationService(db, templates, new BracketTemplateValidationService(),
            NullLogger<TournamentBracketApplicationService>.Instance, Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = true }));
        foreach (var tournamentId in tournamentIds)
        {
            var preview = await applications.PreviewAsync(tournamentId, new TournamentBracketPreviewRequest
            {
                BracketTemplateVersionId = versionId, SeedingMethod = BracketSeedingMethods.RegistrationOrder
            }, default);
            if (tournamentId == doubleId)
            {
                Assert.False(preview.Success);
                Assert.Equal("TEMPLATE_PARTICIPANT_MODE_MISMATCH", preview.ErrorCode);
                continue;
            }
            Assert.True(preview.Success, preview.Message);
            Assert.Equal(7, preview.Data!.MatchCount);
            Assert.All(preview.Data.Seeds, seed =>
            {
                Assert.StartsWith("Đội ", seed.TeamName);
                Assert.Equal(6, seed.Relay!.Members.Count);
                Assert.Equal(new[] { 1, 1, 2, 2, 3, 3 }, seed.Relay.Members.Select(x => x.PairNumber));
            });
            var applied = await applications.ApplyAsync(tournamentId, new ApplyTournamentBracketRequest
            {
                BracketTemplateVersionId = versionId, SeedingMethod = BracketSeedingMethods.RegistrationOrder,
                PreviewHash = preview.Data.PreviewHash, StartAt = TestMatchStartAt,
                RefereeUserId = TestRefereeUserId, AddressText = TestMatchAddress
            }, null, default);
            Assert.True(applied.Success, applied.Message);
            Assert.Equal(7, applied.Data!.GeneratedMatchCount);
        }
        Assert.Equal(14, await db.TournamentGroupMatches.CountAsync());
        Assert.Equal(16, await db.TournamentBracketSeedAssignments.CountAsync());
        Assert.Equal(16, await db.RelayBracketSeedSnapshots.CountAsync());
        var unchangedIds = new[] { doubleId, tournamentIds[2] };
        var otherMatchesBefore = JsonSerializer.Serialize(await db.TournamentGroupMatches.AsNoTracking()
            .Where(x => unchangedIds.Contains(x.TournamentId)).OrderBy(x => x.MatchId)
            .Select(x => new { x.MatchId, x.Team1RegistrationId, x.Team2RegistrationId, x.ScoreTeam1, x.ScoreTeam2, x.IsCompleted }).ToListAsync());
        var lineupsBefore = JsonSerializer.Serialize(await db.RelayTeamMembers.AsNoTracking()
            .OrderBy(x => x.RegistrationId).ThenBy(x => x.Position)
            .Select(x => new { x.RegistrationId, x.Position, x.DisplayName }).ToListAsync());
        var opening = await db.TournamentGroupMatches.FirstAsync(x => x.TournamentId == tournamentIds[1]
            && x.Team1RegistrationId != null && x.Team2RegistrationId != null);
        // A completed relay contributes one parent-match result to the existing propagation graph.
        var at = DateTimeOffset.UtcNow;
        var state = RelayMatchEngine.Start(RelayMatchEngine.Create(opening.MatchId,
            opening.Team1RegistrationId!.Value, opening.Team2RegistrationId!.Value,
            new(6, 40, 600)), at);
        for (var i = 0; i < 38; i++) state = RelayMatchEngine.AwardPoint(state, 2, at.AddSeconds(1));
        for (var i = 0; i < 40; i++) state = RelayMatchEngine.AwardPoint(state, 1, at.AddSeconds(2));
        opening.ScoreTeam1 = state.ScoreTeam1;
        opening.ScoreTeam2 = state.ScoreTeam2;
        opening.IsCompleted = true;
        opening.WinnerRegistrationId = state.WinnerRegistrationId;
        await db.SaveChangesAsync();
        var propagation = new TournamentBracketPropagationService(db, new TournamentStandingsService(db),
            NullLogger<TournamentBracketPropagationService>.Instance);
        await propagation.PropagateFromMatchAsync(opening.MatchId);
        var target = await db.TournamentGroupMatches.FirstAsync(x => x.Team1SourceMatchId == opening.MatchId || x.Team2SourceMatchId == opening.MatchId);
        Assert.Equal(opening.WinnerRegistrationId, target.Team1SourceMatchId == opening.MatchId ? target.Team1RegistrationId : target.Team2RegistrationId);
        Assert.Equal(templateBefore, JsonSerializer.Serialize(await templates.GetGraphAsync(versionId, default)));
        Assert.Equal(otherMatchesBefore, JsonSerializer.Serialize(await db.TournamentGroupMatches.AsNoTracking()
            .Where(x => unchangedIds.Contains(x.TournamentId)).OrderBy(x => x.MatchId)
            .Select(x => new { x.MatchId, x.Team1RegistrationId, x.Team2RegistrationId, x.ScoreTeam1, x.ScoreTeam2, x.IsCompleted }).ToListAsync()));
        Assert.Equal(lineupsBefore, JsonSerializer.Serialize(await db.RelayTeamMembers.AsNoTracking()
            .OrderBy(x => x.RegistrationId).ThenBy(x => x.Position)
            .Select(x => new { x.RegistrationId, x.Position, x.DisplayName }).ToListAsync()));
        Assert.Equal(14, await db.TournamentGroupMatches.CountAsync());
    }
}
