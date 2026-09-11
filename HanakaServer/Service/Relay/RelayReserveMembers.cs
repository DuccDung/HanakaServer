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
        CancellationToken ct = default)
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
            throw new RelayRuleException("ATHLETE_ALREADY_REGISTERED", "Có tài khoản đã thuộc đội khác trong giải, kể cả thành viên dự bị.");
    }
}
