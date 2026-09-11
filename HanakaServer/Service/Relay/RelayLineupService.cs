using System.Data;
using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services.Relay;

public sealed record RelayMemberInput(int Position, long? UserId, string DisplayName, string? AvatarUrl = null);

// Caller must enforce owner/admin authorization. Not registered as a public API yet.
public sealed class RelayLineupService
{
    private readonly PickleballDbContext db;
    private readonly RelayMatchLineupSnapshotService snapshots;

    public RelayLineupService(PickleballDbContext db, RelayMatchLineupSnapshotService snapshots)
    {
        this.db = db;
        this.snapshots = snapshots;
    }

    public async Task<RelayTeam> SaveDraftAsync(long registrationId, long tournamentId, string teamName,
        long? captainUserId, IReadOnlyList<RelayMemberInput> members, long expectedVersion, CancellationToken ct)
    {
        if (expectedVersion < 0 || string.IsNullOrWhiteSpace(teamName) || teamName.Trim().Length > 150)
            throw new RelayRuleException("TEAM_INVALID", "Tên đội hoặc phiên bản không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockRegistrationAsync(registrationId, ct);
        var tournamentMatches = await db.TournamentRegistrations
            .AnyAsync(x => x.RegistrationId == registrationId && x.TournamentId == tournamentId && !x.IsVirtualTeam, ct);
        if (!tournamentMatches)
            throw new RelayRuleException("REGISTRATION_INVALID", "Không tìm thấy đăng ký đội thật trong giải này.");
        var settings = await db.RelayTournamentSettings.SingleOrDefaultAsync(x => x.TournamentId == tournamentId, ct)
            ?? throw new RelayRuleException("RELAY_NOT_CONFIGURED", "Giải chưa có cấu hình tiếp sức.");
        ValidateMembers(members, settings.TeamSize, false);
        var userIds = members.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).ToArray();
        if (await db.Users.CountAsync(x => userIds.Contains(x.UserId), ct) != userIds.Length
            || (captainUserId.HasValue && !await db.Users.AnyAsync(x => x.UserId == captainUserId, ct)))
            throw new RelayRuleException("USER_NOT_FOUND", "Thành viên hoặc đội trưởng không tồn tại.");
        if (captainUserId.HasValue && !userIds.Contains(captainUserId.Value))
            throw new RelayRuleException("CAPTAIN_INVALID", "Đội trưởng phải là một thành viên có tài khoản trong đội.");
        var duplicateUserId = await (
            from member in db.RelayTeamMembers.AsNoTracking()
            join otherTeam in db.RelayTeams.AsNoTracking()
                on member.RegistrationId equals otherTeam.RegistrationId
            where otherTeam.TournamentId == tournamentId
                  && otherTeam.RegistrationId != registrationId
                  && member.UserId.HasValue
                  && userIds.Contains(member.UserId ?? 0)
            select member.UserId ?? 0)
            .FirstOrDefaultAsync(ct);
        if (duplicateUserId > 0)
            throw new RelayRuleException("ATHLETE_ALREADY_REGISTERED",
                $"Tài khoản {duplicateUserId} đã thuộc một đội khác trong giải này.");

        var team = await db.RelayTeams.Include(x => x.Members).SingleOrDefaultAsync(x => x.RegistrationId == registrationId, ct);
        var reserves = await db.RelayTeamReserveMembers.AsNoTracking()
            .Where(x => x.RegistrationId == registrationId).Select(x => x.UserId).ToListAsync(ct);
        await RelayReserveMembers.ValidateAssignmentsAsync(db, tournamentId, registrationId,
            members.Select(x => x.UserId), reserves, ct);
        if ((team?.Version ?? 0) != expectedVersion)
            throw new RelayRuleException("VERSION_CONFLICT", "Đội đã thay đổi; tải lại trước khi sửa.");
        if (team != null)
            await snapshots.CapturePlayedMatchesForTeamAsync(team.TournamentId, team.RegistrationId, ct);
        if (team == null)
        {
            team = new RelayTeam { RegistrationId = registrationId, TournamentId = tournamentId };
            db.RelayTeams.Add(team);
        }
        else
        {
            // Flush deletions first to allow swapping positions without transient unique-user conflicts.
            db.RelayTeamMembers.RemoveRange(team.Members);
            await db.SaveChangesAsync(ct);
            team.Members.Clear();
        }
        team.TeamName = teamName.Trim();
        team.CaptainUserId = captainUserId;
        team.Version = checked(team.Version + 1);
        foreach (var member in members.OrderBy(x => x.Position))
            team.Members.Add(new RelayTeamMember
            {
                RegistrationId = registrationId, Position = member.Position, UserId = member.UserId,
                DisplayName = member.DisplayName.Trim(), AvatarUrl = member.AvatarUrl
            });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return team;
    }

    private Task<int> LockRegistrationAsync(long id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT RegistrationId FROM dbo.TournamentRegistrations WITH (UPDLOCK, HOLDLOCK) WHERE RegistrationId = {id}", ct);

    public static void ValidateMembers(IReadOnlyList<RelayMemberInput> members, int teamSize, bool requireComplete)
    {
        if (teamSize is not (4 or 6 or 8) || members.Count > teamSize || (requireComplete && members.Count != teamSize))
            throw new RelayRuleException("LINEUP_SIZE_INVALID", "Số vận động viên chưa đúng cấu hình giải.");
        if (members.Any(x => x.Position < 1 || x.Position > teamSize || string.IsNullOrWhiteSpace(x.DisplayName)
                             || x.DisplayName.Trim().Length > 150 || x.AvatarUrl?.Length > 500 || x.UserId <= 0)
            || members.Select(x => x.Position).Distinct().Count() != members.Count
            || members.Where(x => x.UserId.HasValue).GroupBy(x => x.UserId).Any(x => x.Count() > 1))
            throw new RelayRuleException("LINEUP_INVALID", "Vị trí, tên hoặc thành viên bị trùng/không hợp lệ.");
    }
}
