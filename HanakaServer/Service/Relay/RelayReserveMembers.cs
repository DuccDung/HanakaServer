using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services.Relay;

public sealed record RelayReserveMemberInput(int Position, long? UserId, string? DisplayName);

// Call inside the registration's serializable transaction, before changing any roster rows.
public static class RelayReserveMembers
{
    public const int Limit = 4;

    public static async Task<List<RelayTeamReserveMember>> ResolveAsync(
        PickleballDbContext db, IReadOnlyList<RelayReserveMemberInput> inputs, CancellationToken ct = default)
    {
        if (inputs.Count > Limit)
            throw new RelayRuleException("RESERVE_LIMIT_EXCEEDED", "Mỗi đội được đăng ký tối đa 4 thành viên dự bị.");
        if (inputs.Any(x => x.Position is < 1 or > Limit || x.UserId <= 0)
            || inputs.Select(x => x.Position).Distinct().Count() != inputs.Count)
            throw new RelayRuleException("RESERVE_INVALID", "Vị trí dự bị phải từ 1 đến 4 và không được trùng.");
        var ids = inputs.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => ids.Contains(x.UserId) && x.IsActive)
            .Select(x => new { x.UserId, x.FullName, x.AvatarUrl }).ToDictionaryAsync(x => x.UserId, ct);
        if (users.Count != ids.Length)
            throw new RelayRuleException("USER_NOT_FOUND", "Có thành viên dự bị không tồn tại hoặc tài khoản đã ngừng hoạt động.");
        return inputs.OrderBy(x => x.Position).Select(x =>
        {
            var user = x.UserId.HasValue ? users[x.UserId.Value] : null;
            var name = (user?.FullName ?? x.DisplayName ?? "").Trim();
            if (name.Length is < 1 or > 150)
                throw new RelayRuleException("RESERVE_INVALID", $"Thành viên dự bị {x.Position} chưa có họ tên hợp lệ.");
            return new RelayTeamReserveMember
            {
                Position = x.Position, UserId = x.UserId, DisplayName = name, AvatarUrl = user?.AvatarUrl
            };
        }).ToList();
    }

    public static async Task ValidateAssignmentsAsync(PickleballDbContext db, long tournamentId,
        long? registrationId, IEnumerable<long?> mainUserIds, IEnumerable<long?> reserveUserIds,
        CancellationToken ct = default, bool includeConflictDetails = false)
    {
        var ids = mainUserIds.Concat(reserveUserIds).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        if (ids.Any(x => x <= 0) || ids.Distinct().Count() != ids.Length)
            throw new RelayRuleException("LINEUP_DUPLICATE_USER", "Một tài khoản chỉ được xuất hiện một lần trong đội, kể cả dự bị.");
        if (ids.Length == 0) return;
        var duplicate = await db.RelayTeamMembers.AnyAsync(x => x.Team.TournamentId == tournamentId
            && (!registrationId.HasValue || x.RegistrationId != registrationId.Value)
            && x.UserId.HasValue && ids.Contains(x.UserId.Value), ct)
            || await db.RelayTeamReserveMembers.AnyAsync(x => x.Team.TournamentId == tournamentId
                && (!registrationId.HasValue || x.RegistrationId != registrationId.Value)
                && x.UserId.HasValue && ids.Contains(x.UserId.Value), ct)
            || await db.RelayTeams.AnyAsync(x => x.TournamentId == tournamentId
                && (!registrationId.HasValue || x.RegistrationId != registrationId.Value)
                && x.CaptainUserId.HasValue && ids.Contains(x.CaptainUserId.Value), ct)
            || await db.TournamentRegistrations.AnyAsync(x => x.TournamentId == tournamentId
                && (!registrationId.HasValue || x.RegistrationId != registrationId.Value)
                && ((x.Player1UserId.HasValue && ids.Contains(x.Player1UserId.Value))
                    || (x.Player2UserId.HasValue && ids.Contains(x.Player2UserId.Value))), ct);
        if (duplicate)
        {
            var message = includeConflictDetails
                ? await DescribeConflictsAsync(db, tournamentId, registrationId, ids, ct)
                : "Có tài khoản đã thuộc đội khác trong giải, kể cả thành viên dự bị.";
            throw new RelayRuleException("ATHLETE_ALREADY_REGISTERED", message);
        }
    }

    // Only the authorized admin form opts into these details. Keep the same transaction and exclusion
    // as validation, and deduplicate the captain/legacy fields that mirror real roster memberships.
    private static async Task<string> DescribeConflictsAsync(PickleballDbContext db, long tournamentId,
        long? registrationId, long[] ids, CancellationToken ct)
    {
        var conflicts = await db.RelayTeamMembers.AsNoTracking()
            .Where(x => x.Team.TournamentId == tournamentId
                && (!registrationId.HasValue || x.RegistrationId != registrationId.Value)
                && x.UserId.HasValue && ids.Contains(x.UserId.Value))
            .Select(x => new AssignmentConflict(x.UserId!.Value, x.RegistrationId, 0, x.Position, x.DisplayName))
            .ToListAsync(ct);
        conflicts.AddRange(await db.RelayTeamReserveMembers.AsNoTracking()
            .Where(x => x.Team.TournamentId == tournamentId
                && (!registrationId.HasValue || x.RegistrationId != registrationId.Value)
                && x.UserId.HasValue && ids.Contains(x.UserId.Value))
            .Select(x => new AssignmentConflict(x.UserId!.Value, x.RegistrationId, 1, x.Position, x.DisplayName))
            .ToListAsync(ct));
        conflicts.AddRange(await db.RelayTeams.AsNoTracking()
            .Where(x => x.TournamentId == tournamentId
                && (!registrationId.HasValue || x.RegistrationId != registrationId.Value)
                && x.CaptainUserId.HasValue && ids.Contains(x.CaptainUserId.Value))
            .Select(x => new AssignmentConflict(x.CaptainUserId!.Value, x.RegistrationId, 2, null, ""))
            .ToListAsync(ct));

        var legacy = await db.TournamentRegistrations.AsNoTracking()
            .Where(x => x.TournamentId == tournamentId
                && (!registrationId.HasValue || x.RegistrationId != registrationId.Value)
                && ((x.Player1UserId.HasValue && ids.Contains(x.Player1UserId.Value))
                    || (x.Player2UserId.HasValue && ids.Contains(x.Player2UserId.Value))))
            .Select(x => new { x.RegistrationId, x.Player1UserId, x.Player1Name, x.Player2UserId, x.Player2Name })
            .ToListAsync(ct);
        foreach (var row in legacy)
        {
            if (row.Player1UserId.HasValue && ids.Contains(row.Player1UserId.Value))
                conflicts.Add(new(row.Player1UserId.Value, row.RegistrationId, 3, 1, row.Player1Name));
            if (row.Player2UserId.HasValue && ids.Contains(row.Player2UserId.Value))
                conflicts.Add(new(row.Player2UserId.Value, row.RegistrationId, 3, 2, row.Player2Name));
        }

        var registrationIds = conflicts.Select(x => x.RegistrationId).Distinct().ToArray();
        var teams = await (
            from registration in db.TournamentRegistrations.AsNoTracking()
            where registration.TournamentId == tournamentId && registrationIds.Contains(registration.RegistrationId)
            join team in db.RelayTeams.AsNoTracking() on registration.RegistrationId equals team.RegistrationId into joinedTeams
            from team in joinedTeams.DefaultIfEmpty()
            select new { registration.RegistrationId, registration.RegCode, TeamName = team == null ? null : team.TeamName }
        ).ToDictionaryAsync(x => x.RegistrationId, ct);
        var names = await db.Users.AsNoTracking().Where(x => ids.Contains(x.UserId))
            .Select(x => new { x.UserId, x.FullName }).ToDictionaryAsync(x => x.UserId, x => x.FullName, ct);

        var lines = conflicts.GroupBy(x => new { x.UserId, x.RegistrationId })
            .Select(group => group.OrderBy(x => x.Kind).ThenBy(x => x.Position).First())
            .OrderBy(x => x.UserId).ThenBy(x => x.RegistrationId)
            .Select(conflict =>
            {
                var name = names.GetValueOrDefault(conflict.UserId);
                if (string.IsNullOrWhiteSpace(name)) name = conflict.DisplayName;
                if (string.IsNullOrWhiteSpace(name)) name = "Tài khoản";
                var team = teams.GetValueOrDefault(conflict.RegistrationId);
                var code = string.IsNullOrWhiteSpace(team?.RegCode) ? $"#{conflict.RegistrationId}" : team.RegCode;
                var teamLabel = string.IsNullOrWhiteSpace(team?.TeamName)
                    ? $"đăng ký {code}" : $"đội “{team.TeamName}” (mã ĐK {code})";
                var role = conflict.Kind switch
                {
                    0 => $"đội hình chính, vị trí {conflict.Position}",
                    1 => $"dự bị, vị trí {conflict.Position}",
                    2 => "đội trưởng",
                    _ => $"VĐV {conflict.Position} trong đăng ký cũ"
                };
                return $"• {name.Trim()} (User ID #{conflict.UserId}) — {teamLabel}; {role}.";
            });
        return "Các tài khoản đã thuộc đội khác trong giải:\n" + string.Join("\n", lines);
    }

    private sealed record AssignmentConflict(long UserId, long RegistrationId, int Kind, int? Position, string? DisplayName);
}
