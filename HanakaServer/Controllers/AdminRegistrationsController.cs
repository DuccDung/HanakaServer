using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Dtos.Payments;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminRegistrationsController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly TournamentRegistrationPaymentService _paymentService;
        private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

        private sealed class UserPlayerSnapshot
        {
            public long UserId { get; init; }
            public string FullName { get; init; } = "";
            public string? AvatarUrl { get; init; }
            public decimal RatingSingle { get; init; }
            public decimal RatingDouble { get; init; }
        }

        private sealed class RegistrationDeletePreparationResult
        {
            public bool CanDelete { get; init; }
            public int StatusCode { get; init; }
            public string Message { get; init; } = string.Empty;
            public int MatchTeamRefCount { get; init; }
            public int MatchWinnerRefCount { get; init; }
            public int ScoreHistoryWinnerRefCount { get; init; }
            public int PrizeRefCount { get; init; }
            public int DeletedPaymentCount { get; init; }
            public int DetachedWebhookCount { get; init; }
            public int DetachedPairRequestCount { get; init; }
        }

        private static readonly HashSet<string> DeletablePaymentStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "pending",
            "expired",
            "failed",
            "cancelled"
        };

        public AdminRegistrationsController(
            PickleballDbContext db,
            IWebHostEnvironment env,
            TournamentRegistrationPaymentService paymentService)
        {
            _db = db;
            _env = env;
            _paymentService = paymentService;
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
                return NotFound(new { message = "Không tìm thấy giải đấu." });

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
                    return NotFound(new { message = "Không tìm thấy giải đấu." });

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
                        message = "Giải đấu đã đủ đội. Hãy tạo đăng ký chờ ghép hoặc tăng số đội dự kiến."
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
                return StatusCode(500, new { message = "Tạo đăng ký thất bại.", detail = ex.Message });
            }
        }

        // =========================
        // PAIR WAITING (FIX binding + transaction + capacity + logic)
        // =========================
        [HttpPost("registrations/{registrationId:long}/pair")]
        public async Task<IActionResult> Pair(long registrationId, [FromBody] PairWaitingDto body)
        {
            if (body == null || body.WithWaitingRegistrationId <= 0)
                return BadRequest(new { message = "Vui lòng chọn đăng ký đang chờ ghép." });

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
                    return BadRequest(new { message = "Cả hai đăng ký phải ở trạng thái chờ ghép." });

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
                    return BadRequest(new { message = "Giải đấu đã đủ đội, không thể ghép thành đội hợp lệ." });

                var deletePreparation = await PrepareRegistrationForHardDeleteAsync(b, HttpContext.RequestAborted);
                if (!deletePreparation.CanDelete)
                {
                    await tx.RollbackAsync();
                    return StatusCode(deletePreparation.StatusCode, new
                    {
                        message = $"Không thể ghép vì đăng ký chờ được chọn không thể xóa: {deletePreparation.Message}"
                    });
                }

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
            catch (DbUpdateException)
            {
                await tx.RollbackAsync();
                return Conflict(new
                {
                    message = "Không thể ghép đội vì dữ liệu liên quan vừa thay đổi. Vui lòng tải lại trang và thử lại."
                });
            }
            catch (Exception)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { message = "Ghép đôi thất bại. Không có dữ liệu nào bị thay đổi." });
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

        [HttpPost("registrations/{id:long}/payment-info")]
        public async Task<IActionResult> PaymentInfo(long id, CancellationToken cancellationToken)
        {
            var reg = await _db.TournamentRegistrations
                .AsNoTracking()
                .Include(x => x.Tournament)
                .FirstOrDefaultAsync(x => x.RegistrationId == id, cancellationToken);

            if (reg == null)
                return NotFound(new { message = "Registration not found." });

            var latestPayment = await _db.TournamentRegistrationPayments
                .AsNoTracking()
                .Where(x => x.RegistrationId == id)
                .OrderByDescending(x => x.PaidAt.HasValue)
                .ThenByDescending(x => x.PaidAt)
                .ThenByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            TournamentPaymentCheckoutResponse? checkout = null;
            var createdOrReused = false;

            if (latestPayment != null)
            {
                checkout = await _paymentService.GetCheckoutByTransactionCodeAsync(
                    latestPayment.TransactionCode,
                    cancellationToken);
            }

            if (!reg.Paid)
            {
                var checkoutResult = await _paymentService.CreateOrReuseAdminCheckoutAsync(id, cancellationToken);
                if (!checkoutResult.Success || checkoutResult.Payment == null)
                    return StatusCode(checkoutResult.StatusCode, new { message = checkoutResult.Message });

                checkout = checkoutResult.Payment;
                createdOrReused = true;
                latestPayment = await _db.TournamentRegistrationPayments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.PaymentId == checkout.PaymentId, cancellationToken);
            }

            var amount = checkout?.Amount
                ?? latestPayment?.Amount
                ?? reg.PaymentAmount
                ?? reg.Tournament.RegistrationFeeAmount;
            var currency = NormalizeCurrency(checkout?.Currency ?? latestPayment?.Currency ?? reg.Tournament.RegistrationFeeCurrency);
            var paidAt = checkout?.PaidAt ?? latestPayment?.PaidAt ?? reg.PaidAt;
            var paidAmount = latestPayment?.PaidAmount ?? reg.PaymentAmount;
            var isCashPayment = latestPayment != null && IsCashPaymentRecord(latestPayment);

            return Ok(new
            {
                registrationId = reg.RegistrationId,
                regCode = reg.RegCode,
                isPaid = reg.Paid,
                isCashPayment,
                mode = reg.Paid ? "paid" : "checkout",
                createdOrReused,
                hasPaymentCode = checkout != null,
                tournament = new
                {
                    reg.TournamentId,
                    title = reg.Tournament.Title,
                    feeAmount = reg.Tournament.RegistrationFeeAmount,
                    feeText = FormatAmount(reg.Tournament.RegistrationFeeAmount, reg.Tournament.RegistrationFeeCurrency),
                    currency = NormalizeCurrency(reg.Tournament.RegistrationFeeCurrency)
                },
                team = new
                {
                    name = BuildTeamName(reg),
                    player1Name = reg.Player1Name,
                    player2Name = string.IsNullOrWhiteSpace(reg.Player2Name) ? null : reg.Player2Name,
                    points = reg.Points
                },
                payment = checkout,
                paymentRecord = latestPayment == null
                    ? null
                    : new
                    {
                        latestPayment.PaymentId,
                        latestPayment.Provider,
                        latestPayment.PaymentMethod,
                        latestPayment.Status,
                        isCashPayment,
                        latestPayment.TransactionCode,
                        latestPayment.ProviderTransactionId,
                        latestPayment.BankCode,
                        latestPayment.BankAccountNo,
                        latestPayment.BankAccountName,
                        latestPayment.TransferContent,
                        latestPayment.QrImageUrl,
                        latestPayment.Amount,
                        amountText = FormatAmount(latestPayment.Amount, latestPayment.Currency),
                        latestPayment.PaidAmount,
                        paidAmountText = paidAmount.HasValue ? FormatAmount(paidAmount.Value, currency) : null,
                        latestPayment.Currency,
                        latestPayment.ExpiredAt,
                        expiredAtText = FormatDateTime(latestPayment.ExpiredAt),
                        latestPayment.PaidAt,
                        paidAtText = FormatDateTime(latestPayment.PaidAt),
                        latestPayment.CreatedAt,
                        createdAtText = FormatDateTime(latestPayment.CreatedAt),
                        latestPayment.UpdatedAt,
                        updatedAtText = FormatDateTime(latestPayment.UpdatedAt)
                    },
                manualPayment = checkout == null
                    ? new
                    {
                        status = reg.Paid ? "paid" : "unpaid",
                        statusTitle = reg.Paid ? "Đã thanh toán" : "Chưa thanh toán",
                        amount,
                        amountText = FormatAmount(amount, currency),
                        paidAmount,
                        paidAmountText = paidAmount.HasValue ? FormatAmount(paidAmount.Value, currency) : null,
                        paidAt,
                        paidAtText = FormatDateTime(paidAt),
                        currency
                    }
                    : null
            });
        }

        [HttpPost("registrations/{id:long}/confirm-cash-payment")]
        public async Task<IActionResult> ConfirmCashPayment(long id, CancellationToken cancellationToken)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            try
            {
                var reg = await _db.TournamentRegistrations
                    .Include(x => x.Tournament)
                    .FirstOrDefaultAsync(x => x.RegistrationId == id, cancellationToken);

                if (reg == null)
                    return NotFound(new { message = "Registration not found." });

                if (!reg.Success || reg.WaitingPair)
                    return BadRequest(new { message = "Chỉ đội đã đăng ký thành công mới được xác nhận thanh toán." });

                if (reg.Paid)
                    return BadRequest(new { message = "Đăng ký này đã được ghi nhận thanh toán." });

                var now = DateTime.UtcNow;
                var amount = reg.Tournament.RegistrationFeeAmount > 0
                    ? reg.Tournament.RegistrationFeeAmount
                    : 0m;
                var currency = NormalizeCurrency(reg.Tournament.RegistrationFeeCurrency);

                var payment = new TournamentRegistrationPayment
                {
                    RegistrationId = reg.RegistrationId,
                    TournamentId = reg.TournamentId,
                    UserId = null,
                    Provider = "sepay",
                    PaymentMethod = "bank_transfer",
                    Status = "paid",
                    TransactionCode = await GenerateManualPaymentCodeAsync(reg.TournamentId, reg.RegistrationId, cancellationToken),
                    ProviderTransactionId = null,
                    BankCode = null,
                    BankAccountNo = null,
                    BankAccountName = null,
                    QrImageUrl = null,
                    TransferContent = "Thanh toán tiền mặt",
                    Amount = amount,
                    PaidAmount = amount,
                    Currency = currency,
                    RawResponse = "Admin xác nhận thanh toán tiền mặt.",
                    ExpiredAt = null,
                    PaidAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _db.TournamentRegistrationPayments.Add(payment);

                reg.Paid = true;
                reg.PaidAt = now;
                reg.PaymentAmount = amount;

                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return Ok(new
                {
                    ok = true,
                    message = "Đã xác nhận thanh toán tiền mặt.",
                    registrationId = reg.RegistrationId,
                    paymentId = payment.PaymentId,
                    transactionCode = payment.TransactionCode,
                    paidAt = payment.PaidAt,
                    paidAtText = FormatDateTime(payment.PaidAt),
                    amount = payment.PaidAmount,
                    amountText = FormatAmount(payment.PaidAmount ?? 0m, payment.Currency)
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(cancellationToken);
                return StatusCode(500, new { message = "Xác nhận thanh toán tiền mặt thất bại.", detail = ex.Message });
            }
        }

        [HttpPost("registrations/{id:long}/cancel-payment-confirmation")]
        public async Task<IActionResult> CancelPaymentConfirmation(long id, CancellationToken cancellationToken)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            try
            {
                var reg = await _db.TournamentRegistrations
                    .FirstOrDefaultAsync(x => x.RegistrationId == id, cancellationToken);

                if (reg == null)
                    return NotFound(new { message = "Registration not found." });

                if (!reg.Paid)
                    return BadRequest(new { message = "Đăng ký này đang ở trạng thái chưa thanh toán." });

                var paidPayments = await _db.TournamentRegistrationPayments
                    .Where(x => x.RegistrationId == id && x.Status == "paid")
                    .OrderByDescending(x => x.PaidAt)
                    .ThenByDescending(x => x.CreatedAt)
                    .ToListAsync(cancellationToken);

                var confirmedSepayPayment = paidPayments.FirstOrDefault(x => !IsCashPaymentRecord(x));
                if (confirmedSepayPayment != null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return Conflict(new
                    {
                        message = "Giao dịch đã được Sepay xác nhận. Không thể dùng chức năng hủy xác nhận nội bộ."
                    });
                }

                var now = DateTime.UtcNow;
                var adminName = string.IsNullOrWhiteSpace(User.Identity?.Name)
                    ? "Admin"
                    : User.Identity.Name.Trim();

                foreach (var payment in paidPayments.Where(IsCashPaymentRecord))
                {
                    payment.Status = "cancelled";
                    payment.UpdatedAt = now;

                    var cancellationAudit = $"Admin {adminName} hủy xác nhận thanh toán nội bộ lúc {now:O}.";
                    payment.RawResponse = string.IsNullOrWhiteSpace(payment.RawResponse)
                        ? cancellationAudit
                        : $"{payment.RawResponse}{Environment.NewLine}{cancellationAudit}";
                }

                reg.Paid = false;
                reg.PaidAt = null;
                reg.PaymentAmount = null;

                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return Ok(new
                {
                    ok = true,
                    message = "Đã hủy xác nhận thanh toán.",
                    registrationId = reg.RegistrationId,
                    cancelledCashPayments = paidPayments.Count(IsCashPaymentRecord)
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(cancellationToken);
                return StatusCode(500, new { message = "Hủy xác nhận thanh toán thất bại.", detail = ex.Message });
            }
        }

        [HttpPost("registrations/{id:long}/sync-levels")]
        public async Task<IActionResult> SyncLevels(long id)
        {
            try
            {
                var reg = await _db.TournamentRegistrations.FirstOrDefaultAsync(x => x.RegistrationId == id);
                if (reg == null) return NotFound(new { message = "Registration not found." });

                var tournament = await _db.Tournaments.AsNoTracking()
                    .Where(x => x.TournamentId == reg.TournamentId)
                    .Select(x => new { x.GameType, x.GenderCategory })
                    .FirstOrDefaultAsync();

                if (tournament == null)
                    return NotFound(new { message = "Không tìm thấy giải đấu." });

                var gameType = TournamentTypeHelper.NormalizeGameType(tournament.GameType, tournament.GenderCategory);
                var hasUserPlayer = false;

                if (reg.Player1UserId.HasValue)
                {
                    var p1 = await LoadUserPlayerSnapshotAsync(reg.Player1UserId.Value);
                    reg.Player1Level = PickRating(gameType, p1);
                    hasUserPlayer = true;
                }

                if (gameType == "DOUBLE" && reg.Player2UserId.HasValue)
                {
                    var p2 = await LoadUserPlayerSnapshotAsync(reg.Player2UserId.Value);
                    reg.Player2Level = PickRating(gameType, p2);
                    hasUserPlayer = true;
                }

                if (!hasUserPlayer)
                    return BadRequest(new { message = "Đội này không có thành viên USER để đồng bộ trình." });

                reg.Points = CalcPoints(gameType, reg.Player1Level, HasPlayer2(reg) ? reg.Player2Level : (decimal?)null);

                await _db.SaveChangesAsync();
                return Ok(await ToAdminDtoAsync(reg));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("registrations/{id:long}/players")]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> UpdatePlayers(long id, [FromForm] UpdateRegistrationPlayersForm req)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var reg = await _db.TournamentRegistrations.FirstOrDefaultAsync(x => x.RegistrationId == id);
                if (reg == null) return NotFound(new { message = "Registration not found." });

                var tournament = await _db.Tournaments
                    .Where(x => x.TournamentId == reg.TournamentId)
                    .Select(x => new { x.GameType, x.GenderCategory })
                    .FirstOrDefaultAsync();

                if (tournament == null)
                    return NotFound(new { message = "Không tìm thấy giải đấu." });

                var gameType = TournamentTypeHelper.NormalizeGameType(tournament.GameType, tournament.GenderCategory);

                var p1IsUser = req.Player1UserId.HasValue && req.Player1UserId.Value > 0;
                if (!p1IsUser && string.IsNullOrWhiteSpace(req.Player1Name))
                    return BadRequest(new { message = "Player1: require UserId or Name (guest)." });

                if (gameType == "DOUBLE" && !reg.WaitingPair)
                {
                    var p2IsUser = req.Player2UserId.HasValue && req.Player2UserId.Value > 0;
                    if (!p2IsUser && string.IsNullOrWhiteSpace(req.Player2Name))
                        return BadRequest(new { message = "Player2: require UserId or Name (guest) when DOUBLE đủ cặp." });

                    if (p1IsUser && p2IsUser && req.Player1UserId == req.Player2UserId)
                        return BadRequest(new { message = "Player1 và Player2 không được là cùng một UserId." });
                }

                var preserveP1GuestAvatar = !reg.Player1UserId.HasValue ? reg.Player1Avatar : null;
                var preserveP2GuestAvatar = !reg.Player2UserId.HasValue ? reg.Player2Avatar : null;

                await FillPlayer(
                    gameType: gameType,
                    isPlayer1: true,
                    reg: reg,
                    userId: req.Player1UserId,
                    guestName: req.Player1Name,
                    guestLevel: req.Player1Level,
                    guestAvatarFile: req.Player1AvatarFile,
                    existingGuestAvatar: preserveP1GuestAvatar
                );

                if (gameType == "DOUBLE" && !reg.WaitingPair)
                {
                    await FillPlayer(
                        gameType: gameType,
                        isPlayer1: false,
                        reg: reg,
                        userId: req.Player2UserId,
                        guestName: req.Player2Name,
                        guestLevel: req.Player2Level,
                        guestAvatarFile: req.Player2AvatarFile,
                        existingGuestAvatar: preserveP2GuestAvatar
                    );
                }
                else
                {
                    reg.Player2UserId = null;
                    reg.Player2Name = null;
                    reg.Player2Avatar = null;
                    reg.Player2Level = 0m;
                    reg.Player2Verified = false;
                }

                reg.Points = CalcPoints(gameType, reg.Player1Level, HasPlayer2(reg) ? reg.Player2Level : (decimal?)null);

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

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
                return StatusCode(500, new { message = "Update registration players failed (db).", detail = ex.Message });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { message = "Cập nhật vận động viên đăng ký thất bại.", detail = ex.Message });
            }
        }

        // =========================
        // DELETE
        // =========================
        [HttpDelete("registrations/{id:long}")]
        public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var reg = await _db.TournamentRegistrations
                    .FirstOrDefaultAsync(x => x.RegistrationId == id, cancellationToken);

                if (reg == null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return NotFound(new { message = "Registration not found." });
                }

                var preparation = await PrepareRegistrationForHardDeleteAsync(reg, cancellationToken);
                if (!preparation.CanDelete)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return StatusCode(preparation.StatusCode, new
                    {
                        message = preparation.Message,
                        details = new
                        {
                            preparation.MatchTeamRefCount,
                            preparation.MatchWinnerRefCount,
                            preparation.ScoreHistoryWinnerRefCount,
                            preparation.PrizeRefCount
                        }
                    });
                }

                _db.TournamentRegistrations.Remove(reg);
                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return Ok(new
                {
                    ok = true,
                    message = "Đã xóa đăng ký an toàn.",
                    preparation.DeletedPaymentCount,
                    preparation.DetachedWebhookCount,
                    preparation.DetachedPairRequestCount
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(cancellationToken);
                return Conflict(new
                {
                    message = "Không thể xóa đăng ký vì dữ liệu liên quan vừa thay đổi. Vui lòng tải lại trang và thử lại."
                });
            }
            catch (Exception)
            {
                await tx.RollbackAsync(cancellationToken);
                return StatusCode(500, new
                {
                    message = "Xóa đăng ký thất bại. Không có dữ liệu nào bị xóa."
                });
            }
        }

        // =========================
        // HELPERS
        // =========================
        private async Task<RegistrationDeletePreparationResult> PrepareRegistrationForHardDeleteAsync(
            TournamentRegistration registration,
            CancellationToken cancellationToken)
        {
            var registrationId = registration.RegistrationId;

            if (registration.Paid)
            {
                return new RegistrationDeletePreparationResult
                {
                    CanDelete = false,
                    StatusCode = StatusCodes.Status409Conflict,
                    Message = "Đăng ký đang được đánh dấu đã thanh toán. Hãy hủy xác nhận thanh toán trước khi xóa đội."
                };
            }

            var matchTeamRefCount = await _db.TournamentGroupMatches
                .CountAsync(
                    x => x.Team1RegistrationId == registrationId || x.Team2RegistrationId == registrationId,
                    cancellationToken);

            var matchWinnerRefCount = await _db.TournamentGroupMatches
                .CountAsync(x => x.WinnerRegistrationId == registrationId, cancellationToken);

            var scoreHistoryWinnerRefCount = await _db.TournamentMatchScoreHistories
                .CountAsync(x => x.WinnerRegistrationId == registrationId, cancellationToken);

            var prizeRefCount = await _db.TournamentPrizes
                .CountAsync(x => x.RegistrationId == registrationId, cancellationToken);

            if (matchTeamRefCount > 0 ||
                matchWinnerRefCount > 0 ||
                scoreHistoryWinnerRefCount > 0 ||
                prizeRefCount > 0)
            {
                return new RegistrationDeletePreparationResult
                {
                    CanDelete = false,
                    StatusCode = StatusCodes.Status409Conflict,
                    Message = BuildDeleteBlockedMessage(
                        matchTeamRefCount,
                        matchWinnerRefCount,
                        scoreHistoryWinnerRefCount,
                        prizeRefCount),
                    MatchTeamRefCount = matchTeamRefCount,
                    MatchWinnerRefCount = matchWinnerRefCount,
                    ScoreHistoryWinnerRefCount = scoreHistoryWinnerRefCount,
                    PrizeRefCount = prizeRefCount
                };
            }

            var payments = await _db.TournamentRegistrationPayments
                .Include(x => x.TournamentSepayWebhooks)
                .Where(x => x.RegistrationId == registrationId)
                .OrderBy(x => x.PaymentId)
                .ToListAsync(cancellationToken);

            foreach (var payment in payments)
            {
                var status = (payment.Status ?? string.Empty).Trim();
                var isCancelledCashPayment =
                    IsCashPaymentRecord(payment) &&
                    string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);
                var hasProcessedWebhook = payment.TournamentSepayWebhooks.Any(
                    webhook => webhook.IsProcessed || webhook.ProcessedAt.HasValue);
                var hasConfirmedPaymentData =
                    string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase) ||
                    payment.PaidAt.HasValue ||
                    (payment.PaidAmount.HasValue && payment.PaidAmount.Value > 0) ||
                    !string.IsNullOrWhiteSpace(payment.ProviderTransactionId);

                if (hasProcessedWebhook)
                {
                    return new RegistrationDeletePreparationResult
                    {
                        CanDelete = false,
                        StatusCode = StatusCodes.Status409Conflict,
                        Message = "Đăng ký đã có webhook thanh toán được xử lý. Không thể xóa để bảo toàn dữ liệu đối soát."
                    };
                }

                if (string.Equals(status, "processing", StringComparison.OrdinalIgnoreCase))
                {
                    return new RegistrationDeletePreparationResult
                    {
                        CanDelete = false,
                        StatusCode = StatusCodes.Status409Conflict,
                        Message = "Giao dịch thanh toán đang được xử lý. Vui lòng chờ hoàn tất rồi thử lại."
                    };
                }

                if (hasConfirmedPaymentData && !isCancelledCashPayment)
                {
                    return new RegistrationDeletePreparationResult
                    {
                        CanDelete = false,
                        StatusCode = StatusCodes.Status409Conflict,
                        Message = "Đăng ký đã có dữ liệu thanh toán thực tế. Không thể xóa để bảo toàn dữ liệu đối soát."
                    };
                }

                if (!DeletablePaymentStatuses.Contains(status))
                {
                    return new RegistrationDeletePreparationResult
                    {
                        CanDelete = false,
                        StatusCode = StatusCodes.Status409Conflict,
                        Message = $"Giao dịch đang ở trạng thái '{status}'. Trạng thái này không được phép xóa tự động."
                    };
                }
            }

            var webhooksToDetach = payments
                .SelectMany(x => x.TournamentSepayWebhooks)
                .ToList();

            foreach (var webhook in webhooksToDetach)
            {
                webhook.PaymentId = null;
                webhook.Payment = null;
            }

            if (payments.Count > 0)
            {
                _db.TournamentRegistrationPayments.RemoveRange(payments);
            }

            var pairRequests = await _db.TournamentPairRequests
                .Where(x => x.RegistrationId == registrationId)
                .ToListAsync(cancellationToken);

            foreach (var pairRequest in pairRequests)
            {
                pairRequest.RegistrationId = null;
                pairRequest.Registration = null;
            }

            return new RegistrationDeletePreparationResult
            {
                CanDelete = true,
                StatusCode = StatusCodes.Status200OK,
                Message = "Đăng ký có thể được xóa an toàn.",
                DeletedPaymentCount = payments.Count,
                DetachedWebhookCount = webhooksToDetach.Count,
                DetachedPairRequestCount = pairRequests.Count
            };
        }

        private static decimal CalcPoints(string gameType, decimal p1, decimal? p2)
        {
            gameType = (gameType ?? "").ToUpperInvariant();
            return gameType == "DOUBLE" ? (p1 + (p2 ?? 0)) : p1;
        }

        private static string BuildTeamName(TournamentRegistration registration)
        {
            return string.IsNullOrWhiteSpace(registration.Player2Name)
                ? registration.Player1Name
                : $"{registration.Player1Name} / {registration.Player2Name}";
        }

        private static bool IsCashPaymentRecord(TournamentRegistrationPayment payment)
        {
            return payment.TransactionCode.StartsWith("MANUAL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(payment.TransferContent, "Thanh toán tiền mặt", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCurrency(string? currency)
        {
            return string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant();
        }

        private static string FormatAmount(decimal amount, string? currency)
        {
            var normalizedCurrency = NormalizeCurrency(currency);
            return string.Equals(normalizedCurrency, "VND", StringComparison.OrdinalIgnoreCase)
                ? string.Format(ViCulture, "{0:N0} VND", amount)
                : string.Format(ViCulture, "{0:N0} {1}", amount, normalizedCurrency);
        }

        private static string? FormatDateTime(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("HH:mm dd/MM/yyyy", ViCulture)
                : null;
        }

        private static bool HasPlayer2(TournamentRegistration reg)
        {
            return reg.Player2UserId.HasValue || !string.IsNullOrWhiteSpace(reg.Player2Name);
        }

        private static decimal PickRating(string gameType, UserPlayerSnapshot player)
        {
            return string.Equals(gameType, "DOUBLE", StringComparison.OrdinalIgnoreCase)
                ? player.RatingDouble
                : player.RatingSingle;
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
            IFormFile? guestAvatarFile,
            string? existingGuestAvatar = null)
        {
            if (userId.HasValue && userId.Value > 0)
            {
                var u = await LoadUserPlayerSnapshotAsync(userId.Value);
                var pickedLevel = PickRating(gameType, u);

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
                throw new InvalidOperationException("Vui lòng nhập tên khách.");

            var level = guestLevel ?? 0m;

            string? avatarUrl = null;
            if (guestAvatarFile != null && guestAvatarFile.Length > 0)
                avatarUrl = await SaveAvatarFile(guestAvatarFile);
            else
                avatarUrl = existingGuestAvatar;

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

        private async Task<UserPlayerSnapshot> LoadUserPlayerSnapshotAsync(long userId)
        {
            var user = await _db.Users.AsNoTracking()
                .Where(x => x.UserId == userId && x.IsActive)
                .Select(x => new
                {
                    x.UserId,
                    x.FullName,
                    x.AvatarUrl,
                    x.RatingSingle,
                    x.RatingDouble,
                    LatestRating = _db.UserRatingHistories
                        .Where(r => r.UserId == x.UserId)
                        .OrderByDescending(r => r.RatedAt)
                        .ThenByDescending(r => r.RatingHistoryId)
                        .Select(r => new
                        {
                            r.RatingSingle,
                            r.RatingDouble
                        })
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            if (user == null)
                throw new InvalidOperationException($"Không tìm thấy ID người dùng {userId}.");

            return new UserPlayerSnapshot
            {
                UserId = user.UserId,
                FullName = user.FullName,
                AvatarUrl = user.AvatarUrl,
                RatingSingle = user.LatestRating?.RatingSingle ?? user.RatingSingle ?? 0m,
                RatingDouble = user.LatestRating?.RatingDouble ?? user.RatingDouble ?? 0m
            };
        }

        private async Task<string> GenerateManualPaymentCodeAsync(
            long tournamentId,
            long registrationId,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                var token = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                var candidate = $"MANUALT{tournamentId}R{registrationId}{token}";
                var exists = await _db.TournamentRegistrationPayments
                    .AsNoTracking()
                    .AnyAsync(x => x.TransactionCode == candidate, cancellationToken);

                if (!exists)
                {
                    return candidate;
                }
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
                    .Select(x => new
                    {
                        x.RatingSingle,
                        x.RatingDouble,
                        LatestRating = _db.UserRatingHistories
                            .Where(rh => rh.UserId == x.UserId)
                            .OrderByDescending(rh => rh.RatedAt)
                            .ThenByDescending(rh => rh.RatingHistoryId)
                            .Select(rh => new { rh.RatingSingle, rh.RatingDouble })
                            .FirstOrDefault()
                    })
                    .FirstOrDefaultAsync();

                if (u1 != null)
                {
                    p1S = u1.LatestRating?.RatingSingle ?? u1.RatingSingle ?? 0m;
                    p1D = u1.LatestRating?.RatingDouble ?? u1.RatingDouble ?? 0m;
                }
            }

            if (r.Player2UserId.HasValue)
            {
                var u2 = await _db.Users.AsNoTracking()
                    .Where(x => x.UserId == r.Player2UserId.Value)
                    .Select(x => new
                    {
                        x.RatingSingle,
                        x.RatingDouble,
                        LatestRating = _db.UserRatingHistories
                            .Where(rh => rh.UserId == x.UserId)
                            .OrderByDescending(rh => rh.RatedAt)
                            .ThenByDescending(rh => rh.RatingHistoryId)
                            .Select(rh => new { rh.RatingSingle, rh.RatingDouble })
                            .FirstOrDefault()
                    })
                    .FirstOrDefaultAsync();

                if (u2 != null)
                {
                    p2S = u2.LatestRating?.RatingSingle ?? u2.RatingSingle ?? 0m;
                    p2D = u2.LatestRating?.RatingDouble ?? u2.RatingDouble ?? 0m;
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
