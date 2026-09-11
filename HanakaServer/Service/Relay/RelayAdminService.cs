using System.Data;
using HanakaServer.Data;
using HanakaServer.Dtos.Relay;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services.Relay;

public sealed class RelayAdminService(PickleballDbContext db)
{
    public async Task<RelaySettingsDto> SaveSettingsAsync(
        long tournamentId,
        SaveRelaySettingsRequest request,
        CancellationToken ct)
    {
        if (request.TeamSize is not (4 or 6 or 8) || request.TargetScore <= 0 || request.ExpectedVersion < 0)
            throw new RelayRuleException("SETTINGS_INVALID", "Quy mô đội, điểm đích hoặc phiên bản không hợp lệ.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockTournamentAsync(tournamentId, ct);
        if (!await db.Tournaments.AnyAsync(x => x.TournamentId == tournamentId && !x.Remove, ct))
            throw new RelayRuleException("TOURNAMENT_NOT_FOUND", "Không tìm thấy giải đấu.");

        var settings = await db.RelayTournamentSettings.SingleOrDefaultAsync(x => x.TournamentId == tournamentId, ct);
        if (settings is null && await db.TournamentGroupMatches.AnyAsync(x => x.TournamentId == tournamentId, ct))
            throw new RelayRuleException("TOURNAMENT_HAS_MATCHES", "Cần cấu hình tiếp sức trước khi tạo sơ đồ thi đấu.");
        if ((settings?.Version ?? 0) != request.ExpectedVersion)
            throw new RelayRuleException("VERSION_CONFLICT", "Cấu hình đã thay đổi; tải lại trước khi lưu.");
        if (settings is not null && settings.TeamSize != request.TeamSize
            && await db.RelayTeams.AnyAsync(x => x.TournamentId == tournamentId, ct))
            throw new RelayRuleException("TEAM_SIZE_LOCKED", "Giải đã có đội hình; không thể đổi số người mỗi đội.");
        if (settings is not null && settings.TargetScore != request.TargetScore
            && await db.RelayMatchStates.AnyAsync(x => x.TournamentId == tournamentId, ct))
            throw new RelayRuleException("RULES_LOCKED", "Giải đã có trạng thái trận tiếp sức; không thể đổi điểm đích.");

        if (settings is null)
        {
            settings = new RelayTournamentSettings { TournamentId = tournamentId };
            db.RelayTournamentSettings.Add(settings);
        }

        settings.TeamSize = request.TeamSize;
        settings.TargetScore = request.TargetScore;
        settings.LegDurationSeconds = 600;
        settings.DeadlinePolicy = null;
        settings.Version = checked(settings.Version + 1);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return MapSettings(settings);
    }

    public async Task<RelaySettingsDto> ActivateAsync(
        long tournamentId,
        SetRelayEnabledRequest request,
        CancellationToken ct)
    {
        if (request.ExpectedVersion < 0)
            throw new RelayRuleException("SETTINGS_INVALID", "Phiên bản cấu hình không hợp lệ.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockTournamentAsync(tournamentId, ct);
        var settings = await db.RelayTournamentSettings.SingleOrDefaultAsync(x => x.TournamentId == tournamentId, ct)
            ?? throw new RelayRuleException("RELAY_NOT_CONFIGURED", "Giải chưa có cấu hình tiếp sức.");
        if (settings.Version != request.ExpectedVersion)
            throw new RelayRuleException("VERSION_CONFLICT", "Cấu hình đã thay đổi; tải lại trước khi kích hoạt.");
        if (settings.IsEnabled)
        {
            await tx.CommitAsync(ct);
            return MapSettings(settings);
        }
        if (settings.TeamSize is not (4 or 6 or 8) || settings.TargetScore <= 0)
            throw new RelayRuleException("SETTINGS_INVALID", "Cấu hình tiếp sức chưa hợp lệ.");

        var teams = await db.RelayTeams.Include(x => x.Members)
            .Where(x => x.TournamentId == tournamentId).ToListAsync(ct);
        if (teams.Count < 2)
            throw new RelayRuleException("LINEUPS_NOT_READY", "Cần ít nhất hai đội có đội hình đầy đủ.");
        foreach (var team in teams)
        {
            RelayLineupService.ValidateMembers(team.Members
                .Select(x => new RelayMemberInput(x.Position, x.UserId, x.DisplayName, x.AvatarUrl)).ToList(),
                settings.TeamSize, true);
        }

        settings.IsEnabled = true;
        settings.DeadlinePolicy = null;
        settings.Version = checked(settings.Version + 1);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return MapSettings(settings);
    }

    private Task<int> LockTournamentAsync(long tournamentId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT TournamentId FROM dbo.Tournaments WITH (UPDLOCK, HOLDLOCK) WHERE TournamentId = {tournamentId}", ct);

    public static RelaySettingsDto MapSettings(RelayTournamentSettings settings) => new(
        settings.TournamentId,
        settings.TeamSize,
        settings.TeamSize / 2,
        settings.TargetScore,
        settings.LegDurationSeconds,
        settings.IsEnabled,
        settings.Version);

    public static RelayTeamDto MapTeam(RelayTeam team, int? configuredTeamSize = null) => new(
        team.RegistrationId,
        team.TeamName,
        team.CaptainUserId,
        team.LineupLockedAtUtc,
        IsComplete(team.Members, configuredTeamSize),
        team.Version,
        team.Members.OrderBy(x => x.Position)
            .Select(x => new RelayMemberInput(x.Position, x.UserId, x.DisplayName, x.AvatarUrl)).ToArray());

    private static bool IsComplete(ICollection<RelayTeamMember> members, int? configuredTeamSize) =>
        members.Count == configuredTeamSize
        && members.OrderBy(x => x.Position).Select(x => x.Position)
            .SequenceEqual(Enumerable.Range(1, members.Count))
        && members.All(x => !string.IsNullOrWhiteSpace(x.DisplayName));
}
