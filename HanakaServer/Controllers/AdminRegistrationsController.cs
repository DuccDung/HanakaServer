using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Helpers;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminRegistrationsController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IWebHostEnvironment _env;

        public AdminRegistrationsController(PickleballDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // =========================
        // LIST
        // =========================
        [HttpGet("tournaments/{tournamentId:long}/registrations")]
        public async Task<IActionResult> List(long tournamentId, [FromQuery] string tab = "ALL")
        {
            tab = (tab ?? "ALL").Trim().ToUpperInvariant();

            var tournament = await _db.Tournaments
                .AsNoTracking()
                .Where(t => t.TournamentId == tournamentId)
                .Select(t => new { t.ExpectedTeams, t.GameType, t.GenderCategory, t.Title, t.Status })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            var baseQ = _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId);

            var successCount = await baseQ.CountAsync(x => x.Success);
            var waitingCount = await baseQ.CountAsync(x => x.WaitingPair);
            var capacityLeft = Math.Max(0, tournament.ExpectedTeams - successCount);

            var q = baseQ;
            if (tab == "SUCCESS") q = q.Where(x => x.Success);
            else if (tab == "WAITING") q = q.Where(x => x.WaitingPair);

            var tournamentType = TournamentTypeHelper.Resolve(tournament.GameType, tournament.GenderCategory);
            var isDoubleLike = tournamentType.IsDoubleLike;

            // LEFT JOIN Users để lấy avatar/verified
            // LEFT JOIN UserRatingHistories để lấy rating mới nhất
            var rawItems = await (
                from r in q.OrderBy(x => x.RegIndex)

                join u1x in _db.Users on r.Player1UserId equals (long?)u1x.UserId into u1g
                from u1 in u1g.DefaultIfEmpty()

                join u2x in _db.Users on r.Player2UserId equals (long?)u2x.UserId into u2g
                from u2 in u2g.DefaultIfEmpty()

                // Get latest rating history for Player1
                let u1Rating = _db.UserRatingHistories
                    .Where(rh => rh.UserId == u1.UserId)
                    .OrderByDescending(rh => rh.RatedAt)
                    .ThenByDescending(rh => rh.RatingHistoryId)
                    .Select(rh => new { rh.RatingSingle, rh.RatingDouble })
                    .FirstOrDefault()

                // Get latest rating history for Player2
                let u2Rating = _db.UserRatingHistories
                    .Where(rh => rh.UserId == u2.UserId)
                    .OrderByDescending(rh => rh.RatedAt)
                    .ThenByDescending(rh => rh.RatingHistoryId)
                    .Select(rh => new { rh.RatingSingle, rh.RatingDouble })
                    .FirstOrDefault()

                select new RegistrationAdminItemDto
                {
                    RegistrationId = r.RegistrationId,
                    TournamentId = r.TournamentId,
                    RegIndex = r.RegIndex,
                    RegCode = r.RegCode,
                    RegTime = r.RegTime,

                    Player1Name = r.Player1Name,
                    Player1Avatar = r.Player1Avatar,
                    Player1Level = r.Player1Level,
                    Player1Verified = r.Player1Verified,
                    Player1UserId = r.Player1UserId,

                    Player1LevelSingle = (decimal?)(u1Rating != null ? u1Rating.RatingSingle : (r.Player1Level)),
                    Player1LevelDouble = (decimal?)(u1Rating != null ? u1Rating.RatingDouble : (r.Player1Level)),

                    Player2Name = r.Player2Name,
                    Player2Avatar = r.Player2Avatar,
                    Player2Level = r.Player2Level,
                    Player2Verified = r.Player2Verified,
                    Player2UserId = r.Player2UserId,

                    Player2LevelSingle = (decimal?)(u2Rating != null ? u2Rating.RatingSingle : (r.Player2Name != null ? r.Player2Level : 0m)),
                    Player2LevelDouble = (decimal?)(u2Rating != null ? u2Rating.RatingDouble : (r.Player2Name != null ? r.Player2Level : 0m)),

                    Points = r.Points,
                    BtCode = r.BtCode,
                    Paid = r.Paid,
                    WaitingPair = r.WaitingPair,
                    Success = r.Success,
                    CreatedAt = r.CreatedAt
                }
            ).ToListAsync();

            var items = rawItems.Select(item =>
            {
                var player1PickedLevel = ResolvePickedLevel(
                    item.Player1UserId,
                    item.Player1Level,
                    item.Player1LevelSingle,
                    item.Player1LevelDouble,
                    isDoubleLike);

                var player2PickedLevel = ResolveOptionalPickedLevel(
                    item.Player2Name,
                    item.Player2UserId,
                    item.Player2Level,
                    item.Player2LevelSingle,
                    item.Player2LevelDouble,
                    isDoubleLike);

                item.Player1Level = player1PickedLevel;
                item.Player2Level = player2PickedLevel ?? 0m;
                item.Points = CalcPoints(
                    isDoubleLike ? "DOUBLE" : "SINGLE",
                    player1PickedLevel,
                    player2PickedLevel);

                return item;
            }).ToList();

            return Ok(new
            {
                tournament = new
                {
                    tournament.ExpectedTeams,
                    tournament.GameType,
                    GenderCategory = tournamentType.GenderCategory,
                    TournamentTypeCode = tournamentType.TournamentTypeCode,
                    TournamentTypeLabel = tournamentType.TournamentTypeLabel,
                    tournament.Title,
                    tournament.Status
                },
                counts = new { success = successCount, waiting = waitingCount, capacityLeft },
                items
            });
        }
        // =========================
        // CREATE (FIX lỗi 500 + logic capacity + transaction)
        // =========================
        [HttpPost("tournaments/{tournamentId:long}/registrations")]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> Create(long tournamentId, [FromForm] CreateRegistrationForm req)
        {
            // Serializable để tránh trùng RegIndex khi 2 admin tạo cùng lúc
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var tournament = await _db.Tournaments
                    .FirstOrDefaultAsync(t => t.TournamentId == tournamentId);

                if (tournament == null)
                    return NotFound(new { message = "Tournament not found." });

                var gameType = ((req.GameType ?? tournament.GameType ?? "DOUBLE").Trim()).ToUpperInvariant();
                if (gameType != "SINGLE" && gameType != "DOUBLE")
                    return BadRequest(new { message = "Invalid GameType. Use SINGLE/DOUBLE." });

                // SINGLE: luôn Success, không WaitingPair
                if (gameType == "SINGLE")
                    req.WaitingPair = false;

                // Capacity check:
                // - Nếu tạo SUCCESS (SINGLE hoặc DOUBLE đủ cặp) mà full => báo lỗi (admin muốn thêm thì phải để waiting)
                var successCount = await _db.TournamentRegistrations
                    .Where(x => x.TournamentId == tournamentId && x.Success)
                    .CountAsync();

                var capacityLeft = Math.Max(0, tournament.ExpectedTeams - successCount);

                var waitingPair = (gameType == "DOUBLE") && req.WaitingPair;
                var willBeSuccess = (gameType == "SINGLE") || (gameType == "DOUBLE" && !waitingPair);

                if (willBeSuccess && capacityLeft <= 0)
                {
                    return BadRequest(new
                    {
                        message = "Tournament is full. Create as WaitingPair or increase ExpectedTeams."
                    });
                }

                // Validate P1
                var p1IsUser = req.Player1UserId.HasValue && req.Player1UserId.Value > 0;
                if (!p1IsUser && string.IsNullOrWhiteSpace(req.Player1Name))
                    return BadRequest(new { message = "Player1: require UserId or Name (guest)." });

                // Validate DOUBLE đủ cặp
                if (gameType == "DOUBLE" && !waitingPair)
                {
                    var p2IsUser = req.Player2UserId.HasValue && req.Player2UserId.Value > 0;
                    if (!p2IsUser && string.IsNullOrWhiteSpace(req.Player2Name))
                        return BadRequest(new { message = "Player2: require UserId or Name (guest) when DOUBLE đủ cặp." });
                }

                // RegIndex, RegCode
                var maxIndex = await _db.TournamentRegistrations
                    .Where(x => x.TournamentId == tournamentId)
                    .MaxAsync(x => (int?)x.RegIndex) ?? 0;

                var nextIndex = maxIndex + 1;

                var reg = new TournamentRegistration
                {
                    TournamentId = tournamentId,
                    RegIndex = nextIndex,
                    RegCode = $"{tournamentId}-{nextIndex:0000}",
                    RegTime = DateTime.UtcNow,
                    RegTimeRaw = DateTime.UtcNow.ToString("o"),
                    Paid = req.Paid,
                    BtCode = string.IsNullOrWhiteSpace(req.BtCode) ? null : req.BtCode.Trim(),
                    WaitingPair = waitingPair,
                    Success = willBeSuccess,
                    CreatedAt = DateTime.UtcNow
                };

                // Fill P1
                await FillPlayer(
                    gameType: gameType,
                    isPlayer1: true,
                    reg: reg,
                    userId: req.Player1UserId,
                    guestName: req.Player1Name,
                    guestLevel: req.Player1Level,
                    guestAvatarFile: req.Player1AvatarFile
                );

                // Fill P2 khi DOUBLE đủ cặp
                if (gameType == "DOUBLE" && !waitingPair)
                {
                    await FillPlayer(
                        gameType: gameType,
                        isPlayer1: false,
                        reg: reg,
                        userId: req.Player2UserId,
                        guestName: req.Player2Name,
                        guestLevel: req.Player2Level,
                        guestAvatarFile: req.Player2AvatarFile
                    );
                }
                else
                {
                    // DOUBLE waiting: đảm bảo trống P2
                    reg.Player2UserId = null;
                    reg.Player2Name = null;
                    reg.Player2Avatar = null;
                    reg.Player2Level = 0m;
                    reg.Player2Verified = false;
                }

                // Points
                reg.Points = CalcPoints(gameType, reg.Player1Level, reg.Player2Name != null ? reg.Player2Level : (decimal?)null);

                _db.TournamentRegistrations.Add(reg);
                await _db.SaveChangesAsync();

                await tx.CommitAsync();

                // RETURN DTO (tránh 500 serialize entity)
                return Ok(await ToAdminDtoAsync(reg));
            }
            catch (InvalidOperationException ex)
            {
                await tx.RollbackAsync();
                return BadRequest(new { message = ex.Message });
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { message = "Create registration failed (db).", detail = ex.Message });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { message = "Create registration failed.", detail = ex.Message });
            }
        }

        // =========================
        // PAIR WAITING (FIX binding + transaction + capacity + logic)
        // =========================
        [HttpPost("registrations/{registrationId:long}/pair")]
        public async Task<IActionResult> Pair(long registrationId, [FromBody] PairWaitingDto body)
        {
            if (body == null || body.WithWaitingRegistrationId <= 0)
                return BadRequest(new { message = "WithWaitingRegistrationId is required." });

            if (body.WithWaitingRegistrationId == registrationId)
                return BadRequest(new { message = "Cannot pair with itself." });

            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var a = await _db.TournamentRegistrations
                    .FirstOrDefaultAsync(x => x.RegistrationId == registrationId);

                var b = await _db.TournamentRegistrations
                    .FirstOrDefaultAsync(x => x.RegistrationId == body.WithWaitingRegistrationId);

                if (a == null || b == null)
                    return NotFound(new { message = "Registration not found." });

                if (a.TournamentId != b.TournamentId)
                    return BadRequest(new { message = "Different tournament." });

                if (!a.WaitingPair || !b.WaitingPair)
                    return BadRequest(new { message = "Both must be waiting registrations." });

                // a waiting nhưng lỡ có P2 rồi => data bẩn
                if (!string.IsNullOrWhiteSpace(a.Player2Name) || a.Player2UserId.HasValue)
                    return BadRequest(new { message = "Registration A already has Player2." });

                var tournament = await _db.Tournaments
                    .Where(t => t.TournamentId == a.TournamentId)
                    .Select(t => new { t.GameType, t.ExpectedTeams })
                    .FirstOrDefaultAsync();

                var gt = (tournament?.GameType ?? "DOUBLE").ToUpperInvariant();
                if (gt != "DOUBLE")
                    return BadRequest(new { message = "This tournament is not DOUBLE. Pair is not allowed." });

                // capacity check: pairing sẽ biến 2 waiting -> 1 success (tăng success +1)
                var successCount = await _db.TournamentRegistrations
                    .Where(x => x.TournamentId == a.TournamentId && x.Success)
                    .CountAsync();

                var capacityLeft = Math.Max(0, (tournament?.ExpectedTeams ?? 0) - successCount);
                if (capacityLeft <= 0)
                    return BadRequest(new { message = "Tournament is full. Cannot pair into a SUCCESS team." });

                // Merge: a gets b.Player1 as Player2
                a.Player2UserId = b.Player1UserId;
                a.Player2Name = b.Player1Name;
                a.Player2Avatar = b.Player1Avatar;
                a.Player2Level = b.Player1Level;
                a.Player2Verified = b.Player1Verified;

                a.WaitingPair = false;
                a.Success = true;

                a.Points = CalcPoints("DOUBLE", a.Player1Level, a.Player2Level);

                // remove b
                _db.TournamentRegistrations.Remove(b);

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(await ToAdminDtoAsync(a));
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { message = "Pair failed (db).", detail = ex.Message });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { message = "Pair failed.", detail = ex.Message });
            }
        }

        // =========================
        // UPDATE (Return DTO)
        // =========================
        [HttpPut("registrations/{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateRegistrationDto dto)
        {
            var reg = await _db.TournamentRegistrations.FirstOrDefaultAsync(x => x.RegistrationId == id);
            if (reg == null) return NotFound(new { message = "Registration not found." });

            if (dto.Paid.HasValue) reg.Paid = dto.Paid.Value;
            if (dto.BtCode != null) reg.BtCode = string.IsNullOrWhiteSpace(dto.BtCode) ? null : dto.BtCode.Trim();

            await _db.SaveChangesAsync();
            return Ok(await ToAdminDtoAsync(reg));
        }

        // =========================
        // DELETE
        // =========================
        [HttpDelete("registrations/{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var reg = await _db.TournamentRegistrations.FirstOrDefaultAsync(x => x.RegistrationId == id);
            if (reg == null) return NotFound(new { message = "Registration not found." });

            var matchTeamRefCount = await _db.TournamentGroupMatches
                .CountAsync(x => x.Team1RegistrationId == id || x.Team2RegistrationId == id);

            var matchWinnerRefCount = await _db.TournamentGroupMatches
                .CountAsync(x => x.WinnerRegistrationId == id);

            var scoreHistoryWinnerRefCount = await _db.TournamentMatchScoreHistories
                .CountAsync(x => x.WinnerRegistrationId == id);

            var prizeRefCount = await _db.TournamentPrizes
                .CountAsync(x => x.RegistrationId == id);

            if (matchTeamRefCount > 0 || matchWinnerRefCount > 0 || scoreHistoryWinnerRefCount > 0 || prizeRefCount > 0)
            {
                return BadRequest(new
                {
                    message = BuildDeleteBlockedMessage(
                        matchTeamRefCount,
                        matchWinnerRefCount,
                        scoreHistoryWinnerRefCount,
                        prizeRefCount),
                    details = new
                    {
                        matchTeamRefCount,
                        matchWinnerRefCount,
                        scoreHistoryWinnerRefCount,
                        prizeRefCount
                    }
                });
            }

            var pairRequests = await _db.TournamentPairRequests
                .Where(x => x.RegistrationId == id)
                .ToListAsync();

            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            foreach (var pairRequest in pairRequests)
                pairRequest.RegistrationId = null;

            _db.TournamentRegistrations.Remove(reg);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new
            {
                ok = true,
                detachedPairRequestCount = pairRequests.Count
            });
        }

        // =========================
        // HELPERS
        // =========================
        private static decimal CalcPoints(string gameType, decimal p1, decimal? p2)
        {
            gameType = (gameType ?? "").ToUpperInvariant();
            return gameType == "DOUBLE" ? (p1 + (p2 ?? 0)) : p1;
        }

        private static decimal ResolvePickedLevel(
            long? userId,
            decimal storedLevel,
            decimal? ratingSingle,
            decimal? ratingDouble,
            bool isDoubleLike)
        {
            if (!userId.HasValue)
                return storedLevel;

            // If storedLevel > 0, use it (it's the snapshot at registration time)
            // Only fallback to User rating if storedLevel is 0 (legacy data)
            if (storedLevel > 0)
                return storedLevel;

            var picked = isDoubleLike ? ratingDouble : ratingSingle;
            return picked ?? storedLevel;
        }

        private static decimal? ResolveOptionalPickedLevel(
            string? playerName,
            long? userId,
            decimal storedLevel,
            decimal? ratingSingle,
            decimal? ratingDouble,
            bool isDoubleLike)
        {
            if (!userId.HasValue && string.IsNullOrWhiteSpace(playerName))
                return null;

            return ResolvePickedLevel(userId, storedLevel, ratingSingle, ratingDouble, isDoubleLike);
        }

        private static string BuildDeleteBlockedMessage(
            int matchTeamRefCount,
            int matchWinnerRefCount,
            int scoreHistoryWinnerRefCount,
            int prizeRefCount)
        {
            var blockers = new List<string>();

            if (matchTeamRefCount > 0)
                blockers.Add($"{matchTeamRefCount} trận đấu đang dùng đội này");

            if (matchWinnerRefCount > 0)
                blockers.Add($"{matchWinnerRefCount} trận đấu đang ghi nhận đội này là bên thắng");

            if (scoreHistoryWinnerRefCount > 0)
                blockers.Add($"{scoreHistoryWinnerRefCount} lịch sử chấm điểm đang ghi nhận đội này là bên thắng");

            if (prizeRefCount > 0)
                blockers.Add($"{prizeRefCount} giải thưởng đang gán cho đội này");

            var blockerText = blockers.Count > 0
                ? string.Join("; ", blockers)
                : "registration đang được dữ liệu khác tham chiếu";

            return $"Không thể xoá đăng ký này vì {blockerText}. Hãy gỡ các liên kết đó trước rồi thử lại.";
        }

        private static RegistrationAdminItemDto ToAdminDto(TournamentRegistration r)
        {
            return new RegistrationAdminItemDto
            {
                RegistrationId = r.RegistrationId,
                TournamentId = r.TournamentId,
                RegIndex = r.RegIndex,
                RegCode = r.RegCode,
                RegTime = r.RegTime,

                Player1Name = r.Player1Name,
                Player1Avatar = r.Player1Avatar,
                Player1Level = r.Player1Level,
                Player1Verified = r.Player1Verified,
                Player1UserId = r.Player1UserId,

                Player2Name = r.Player2Name,
                Player2Avatar = r.Player2Avatar,
                Player2Level = r.Player2Level,
                Player2Verified = r.Player2Verified,
                Player2UserId = r.Player2UserId,

                Points = r.Points,
                BtCode = r.BtCode,
                Paid = r.Paid,
                WaitingPair = r.WaitingPair,
                Success = r.Success,
                CreatedAt = r.CreatedAt
            };
        }

        // NOTE: lấy đúng rating theo gameType
        private async Task FillPlayer(
            string gameType,
            bool isPlayer1,
            TournamentRegistration reg,
            long? userId,
            string? guestName,
            decimal? guestLevel,
            IFormFile? guestAvatarFile)
        {
            if (userId.HasValue && userId.Value > 0)
            {
                var u = await _db.Users
                    .Where(x => x.UserId == userId.Value && x.IsActive)
                    .Select(x => new
                    {
                        x.UserId,
                        x.FullName,
                        x.AvatarUrl,
                        ratingSingle = x.RatingSingle ?? 0m,
                        ratingDouble = x.RatingDouble ?? 0m
                    })
                    .FirstOrDefaultAsync();

                if (u == null) throw new InvalidOperationException($"UserId {userId.Value} not found.");

                var pickedLevel = (gameType == "DOUBLE") ? u.ratingDouble : u.ratingSingle;

                if (isPlayer1)
                {
                    reg.Player1UserId = u.UserId;
                    reg.Player1Name = u.FullName;
                    reg.Player1Avatar = u.AvatarUrl;
                    reg.Player1Level = pickedLevel;
                    reg.Player1Verified = true;
                }
                else
                {
                    reg.Player2UserId = u.UserId;
                    reg.Player2Name = u.FullName;
                    reg.Player2Avatar = u.AvatarUrl;
                    reg.Player2Level = pickedLevel;
                    reg.Player2Verified = true;
                }

                return;
            }

            // guest
            var name = (guestName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Guest name is required.");

            var level = guestLevel ?? 0m;

            string? avatarUrl = null;
            if (guestAvatarFile != null && guestAvatarFile.Length > 0)
                avatarUrl = await SaveAvatarFile(guestAvatarFile);

            if (isPlayer1)
            {
                reg.Player1UserId = null;
                reg.Player1Name = name;
                reg.Player1Level = level;
                reg.Player1Avatar = avatarUrl;
                reg.Player1Verified = false;
            }
            else
            {
                reg.Player2UserId = null;
                reg.Player2Name = name;
                reg.Player2Level = level;
                reg.Player2Avatar = avatarUrl;
                reg.Player2Verified = false;
            }
        }

        private async Task<string> SaveAvatarFile(IFormFile file)
        {
            var root = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var dir = Path.Combine(root, "uploads", "avatars");
            Directory.CreateDirectory(dir);

            var ext = Path.GetExtension(file.FileName);
            var safeExt = string.IsNullOrWhiteSpace(ext) ? ".jpg" : ext.ToLowerInvariant();

            var fileName = $"{Guid.NewGuid():N}{safeExt}";
            var path = Path.Combine(dir, fileName);

            await using (var fs = new FileStream(path, FileMode.Create))
            {
                await file.CopyToAsync(fs);
            }

            return $"/uploads/avatars/{fileName}";
        }
        private async Task<RegistrationAdminItemDto> ToAdminDtoAsync(TournamentRegistration r)
        {
            decimal? p1S = null, p1D = null, p2S = null, p2D = null;

            if (r.Player1UserId.HasValue)
            {
                var u1 = await _db.Users.AsNoTracking()
                    .Where(x => x.UserId == r.Player1UserId.Value)
                    .Select(x => new { x.RatingSingle, x.RatingDouble })
                    .FirstOrDefaultAsync();

                if (u1 != null)
                {
                    p1S = u1.RatingSingle ?? 0m;
                    p1D = u1.RatingDouble ?? 0m;
                }
            }

            if (r.Player2UserId.HasValue)
            {
                var u2 = await _db.Users.AsNoTracking()
                    .Where(x => x.UserId == r.Player2UserId.Value)
                    .Select(x => new { x.RatingSingle, x.RatingDouble })
                    .FirstOrDefaultAsync();

                if (u2 != null)
                {
                    p2S = u2.RatingSingle ?? 0m;
                    p2D = u2.RatingDouble ?? 0m;
                }
            }

            // Guest / fallback: dùng level đang lưu
            p1S ??= r.Player1Level;
            p1D ??= r.Player1Level;

            if (!string.IsNullOrWhiteSpace(r.Player2Name))
            {
                p2S ??= r.Player2Level;
                p2D ??= r.Player2Level;
            }

            var tournamentType = await _db.Tournaments.AsNoTracking()
                .Where(x => x.TournamentId == r.TournamentId)
                .Select(x => new { x.GameType, x.GenderCategory })
                .FirstOrDefaultAsync();

            var isDoubleLike = TournamentTypeHelper.IsDoubleLike(
                tournamentType?.GameType,
                tournamentType?.GenderCategory);

            var player1PickedLevel = ResolvePickedLevel(
                r.Player1UserId,
                r.Player1Level,
                p1S,
                p1D,
                isDoubleLike);

            var player2PickedLevel = ResolveOptionalPickedLevel(
                r.Player2Name,
                r.Player2UserId,
                r.Player2Level,
                p2S,
                p2D,
                isDoubleLike);

            return new RegistrationAdminItemDto
            {
                RegistrationId = r.RegistrationId,
                TournamentId = r.TournamentId,
                RegIndex = r.RegIndex,
                RegCode = r.RegCode,
                RegTime = r.RegTime,

                Player1Name = r.Player1Name,
                Player1Avatar = r.Player1Avatar,
                Player1Level = player1PickedLevel,
                Player1Verified = r.Player1Verified,
                Player1UserId = r.Player1UserId,
                Player1LevelSingle = p1S,
                Player1LevelDouble = p1D,

                Player2Name = r.Player2Name,
                Player2Avatar = r.Player2Avatar,
                Player2Level = player2PickedLevel ?? 0m,
                Player2Verified = r.Player2Verified,
                Player2UserId = r.Player2UserId,
                Player2LevelSingle = p2S,
                Player2LevelDouble = p2D,

                Points = CalcPoints(
                    isDoubleLike ? "DOUBLE" : "SINGLE",
                    player1PickedLevel,
                    player2PickedLevel),
                BtCode = r.BtCode,
                Paid = r.Paid,
                WaitingPair = r.WaitingPair,
                Success = r.Success,
                CreatedAt = r.CreatedAt
            };
        }
    }
}
