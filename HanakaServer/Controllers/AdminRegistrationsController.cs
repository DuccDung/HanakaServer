using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        [HttpGet("tournaments/{tournamentId:long}/registrations")]
        public async Task<IActionResult> List(long tournamentId, [FromQuery] string tab = "ALL")
        {
            tab = (tab ?? "ALL").ToUpperInvariant();
            var q = _db.TournamentRegistrations.Where(x => x.TournamentId == tournamentId);

            var successCount = await q.CountAsync(x => x.Success);
            var waitingCount = await q.CountAsync(x => x.WaitingPair);

            var tournament = await _db.Tournaments
                .Where(t => t.TournamentId == tournamentId)
                .Select(t => new { t.ExpectedTeams, t.GameType, t.Title, t.Status })
                .FirstOrDefaultAsync();

            if (tournament == null) return NotFound(new { message = "Tournament not found." });

            var capacityLeft = Math.Max(0, tournament.ExpectedTeams - successCount);

            if (tab == "SUCCESS") q = q.Where(x => x.Success);
            else if (tab == "WAITING") q = q.Where(x => x.WaitingPair);

            var items = await q
                .OrderBy(x => x.RegIndex)
                .Select(x => new
                {
                    x.RegistrationId,
                    x.RegIndex,
                    x.RegCode,
                    x.RegTime,
                    x.Player1Name,
                    x.Player1Avatar,
                    x.Player1Level,
                    x.Player1Verified,
                    x.Player1UserId,
                    x.Player2Name,
                    x.Player2Avatar,
                    x.Player2Level,
                    x.Player2Verified,
                    x.Player2UserId,
                    x.Points,
                    x.BtCode,
                    x.Paid,
                    x.WaitingPair,
                    x.Success
                })
                .ToListAsync();

            return Ok(new
            {
                tournament,
                counts = new { success = successCount, waiting = waitingCount, capacityLeft },
                items
            });
        }

        // POST multipart/form-data
        [HttpPost("tournaments/{tournamentId:long}/registrations")]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> Create(long tournamentId, [FromForm] CreateRegistrationForm req)
        {
            var tournament = await _db.Tournaments.FirstOrDefaultAsync(t => t.TournamentId == tournamentId);
            if (tournament == null) return NotFound(new { message = "Tournament not found." });

            var gameType = (req.GameType ?? tournament.GameType ?? "DOUBLE").ToUpperInvariant();
            if (gameType != "SINGLE" && gameType != "DOUBLE")
                return BadRequest(new { message = "Invalid GameType." });

            // validate player1 input
            var p1IsUser = req.Player1UserId.HasValue && req.Player1UserId.Value > 0;
            if (!p1IsUser && string.IsNullOrWhiteSpace(req.Player1Name))
                return BadRequest(new { message = "Player1: require UserId or Name (guest)." });

            // validate double
            var waitingPair = (gameType == "DOUBLE") && req.WaitingPair;
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
                Success = (gameType == "SINGLE") || (gameType == "DOUBLE" && !waitingPair),
                CreatedAt = DateTime.UtcNow
            };

            // Fill Player1
            await FillPlayer(
                isPlayer1: true,
                reg: reg,
                userId: req.Player1UserId,
                guestName: req.Player1Name,
                guestLevel: req.Player1Level,
                guestAvatarFile: req.Player1AvatarFile
            );

            // Fill Player2 if needed
            if (gameType == "DOUBLE" && !waitingPair)
            {
                await FillPlayer(
                    isPlayer1: false,
                    reg: reg,
                    userId: req.Player2UserId,
                    guestName: req.Player2Name,
                    guestLevel: req.Player2Level,
                    guestAvatarFile: req.Player2AvatarFile
                );
            }

            // points
            reg.Points = CalcPoints(gameType, reg.Player1Level, reg.Player2Name != null ? reg.Player2Level : null);

            _db.TournamentRegistrations.Add(reg);
            await _db.SaveChangesAsync();

            return Ok(reg);
        }

        [HttpPost("registrations/{registrationId:long}/pair")]
        public async Task<IActionResult> Pair(long registrationId, [FromBody] dynamic body)
        {
            long withId = (long)(body?.withWaitingRegistrationId ?? 0);
            if (withId <= 0) return BadRequest(new { message = "withWaitingRegistrationId is required." });

            var a = await _db.TournamentRegistrations.FirstOrDefaultAsync(x => x.RegistrationId == registrationId);
            var b = await _db.TournamentRegistrations.FirstOrDefaultAsync(x => x.RegistrationId == withId);

            if (a == null || b == null) return NotFound(new { message = "Registration not found." });
            if (a.TournamentId != b.TournamentId) return BadRequest(new { message = "Different tournament." });
            if (!a.WaitingPair || !b.WaitingPair) return BadRequest(new { message = "Both must be waiting registrations." });

            // merge: a gets b as player2
            a.Player2UserId = b.Player1UserId;
            a.Player2Name = b.Player1Name;
            a.Player2Avatar = b.Player1Avatar;
            a.Player2Level = b.Player1Level;
            a.Player2Verified = b.Player1Verified;

            a.WaitingPair = false;
            a.Success = true;
            a.Points = a.Player1Level + a.Player2Level;

            _db.TournamentRegistrations.Remove(b);
            await _db.SaveChangesAsync();

            return Ok(a);
        }

        [HttpPut("registrations/{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] dynamic body)
        {
            var reg = await _db.TournamentRegistrations.FirstOrDefaultAsync(x => x.RegistrationId == id);
            if (reg == null) return NotFound();

            if (body?.paid != null) reg.Paid = (bool)body.paid;
            if (body?.btCode != null) reg.BtCode = (string)body.btCode;

            await _db.SaveChangesAsync();
            return Ok(reg);
        }

        [HttpDelete("registrations/{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var reg = await _db.TournamentRegistrations.FirstOrDefaultAsync(x => x.RegistrationId == id);
            if (reg == null) return NotFound();

            _db.TournamentRegistrations.Remove(reg);
            await _db.SaveChangesAsync();
            return Ok(new { ok = true });
        }

        private static decimal CalcPoints(string gameType, decimal p1, decimal? p2)
        {
            gameType = (gameType ?? "").ToUpperInvariant();
            return gameType == "DOUBLE" ? (p1 + (p2 ?? 0)) : p1;
        }

        private async Task FillPlayer(bool isPlayer1, TournamentRegistration reg, long? userId, string? guestName, decimal? guestLevel, IFormFile? guestAvatarFile)
        {
            if (userId.HasValue && userId.Value > 0)
            {
                // lấy từ DB user
                var u = await _db.Users
                    .Where(x => x.UserId == userId.Value)
                    .Select(x => new { x.UserId, x.FullName, x.AvatarUrl, x.RatingSingle })
                    .FirstOrDefaultAsync();

                if (u == null) throw new InvalidOperationException($"UserId {userId.Value} not found.");

                if (isPlayer1)
                {
                    reg.Player1UserId = u.UserId;
                    reg.Player1Name = u.FullName;
                    reg.Player1Avatar = u.AvatarUrl;
                    reg.Player1Level = u.RatingSingle ?? 0;
                    reg.Player1Verified = true;
                }
                else
                {
                    reg.Player2UserId = u.UserId;
                    reg.Player2Name = u.FullName;
                    reg.Player2Avatar = u.AvatarUrl;
                    reg.Player2Level = u.RatingSingle ?? 0;
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
            {
                avatarUrl = await SaveAvatarFile(guestAvatarFile);
            }

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
            // lưu vào wwwroot/uploads/avatars
            var dir = Path.Combine(_env.WebRootPath, "uploads", "avatars");
            Directory.CreateDirectory(dir);

            var ext = Path.GetExtension(file.FileName);
            var safeExt = string.IsNullOrWhiteSpace(ext) ? ".jpg" : ext.ToLowerInvariant();

            var fileName = $"{Guid.NewGuid():N}{safeExt}";
            var path = Path.Combine(dir, fileName);

            using (var fs = new FileStream(path, FileMode.Create))
            {
                await file.CopyToAsync(fs);
            }

            // return URL public
            return $"/uploads/avatars/{fileName}";
        }
    }
}