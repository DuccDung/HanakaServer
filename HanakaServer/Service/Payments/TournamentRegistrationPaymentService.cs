using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HanakaServer.Data;
using HanakaServer.Dtos.Payments;
using HanakaServer.Models;
using HanakaServer.Options;
using HanakaServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HanakaServer.Services.Payments;

public sealed class TournamentRegistrationPaymentService
{
    private static readonly string[] ReusableStatuses = new[] { "pending", "processing", "paid" };
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly PickleballDbContext _db;
    private readonly SepayGatewayClient _sepayGatewayClient;
    private readonly SepayOptions _options;
    private readonly PublicRealtimeHub _publicRealtimeHub;
    private readonly ILogger<TournamentRegistrationPaymentService> _logger;

    public TournamentRegistrationPaymentService(
        PickleballDbContext db,
        SepayGatewayClient sepayGatewayClient,
        IOptions<SepayOptions> sepayOptions,
        PublicRealtimeHub publicRealtimeHub,
        ILogger<TournamentRegistrationPaymentService> logger)
    {
        _db = db;
        _sepayGatewayClient = sepayGatewayClient;
        _options = sepayOptions.Value;
        _publicRealtimeHub = publicRealtimeHub;
        _logger = logger;
    }

    public async Task<TournamentPaymentServiceResult> CreateOrReuseCheckoutAsync(
        long userId,
        long registrationId,
        CancellationToken cancellationToken = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var registration = await _db.TournamentRegistrations
                .Include(item => item.Tournament)
                .FirstOrDefaultAsync(item => item.RegistrationId == registrationId, cancellationToken);

            if (registration is null || registration.Tournament.Remove || registration.Tournament.Status == "DRAFT")
            {
                return TournamentPaymentServiceResult.Fail("Không tìm thấy đăng ký giải đấu.", StatusCodes.Status404NotFound);
            }

            if (!registration.Success || registration.WaitingPair)
            {
                return TournamentPaymentServiceResult.Fail("Chỉ đội đã ghép cặp thành công mới có thể thanh toán.");
            }

            var feeAmount = NormalizeAmount(registration.Tournament.RegistrationFeeAmount);
            if (feeAmount <= 0)
            {
                return TournamentPaymentServiceResult.Fail("Giải đấu chưa cấu hình phí đăng ký.", StatusCodes.Status409Conflict);
            }

            var existingPayment = await FindReusablePaymentAsync(registration.RegistrationId, feeAmount, cancellationToken);
            var now = DateTime.UtcNow;

            if (registration.Paid)
            {
                if (existingPayment is not null)
                {
                    var paidResponse = MapCheckoutResponse(existingPayment, registration, reusedExistingPayment: true);
                    await transaction.CommitAsync(cancellationToken);
                    return TournamentPaymentServiceResult.Ok(paidResponse, "Đăng ký đã thanh toán.");
                }

                return TournamentPaymentServiceResult.Fail("Đăng ký này đã được đánh dấu đã thanh toán.", StatusCodes.Status409Conflict);
            }

            if (existingPayment is not null)
            {
                if (IsExpired(existingPayment, now))
                {
                    existingPayment.Status = "expired";
                    existingPayment.UpdatedAt = now;
                }
                else
                {
                    var reusableResponse = MapCheckoutResponse(existingPayment, registration, reusedExistingPayment: true);
                    await transaction.CommitAsync(cancellationToken);
                    return TournamentPaymentServiceResult.Ok(reusableResponse, "Đã tải lại mã thanh toán hiện có.");
                }
            }

            var transactionCode = await GenerateUniqueTransactionCodeAsync(
                registration.TournamentId,
                registration.RegistrationId,
                cancellationToken);
            var transferContent = transactionCode;
            var checkout = await _sepayGatewayClient.PrepareCheckoutAsync(
                feeAmount,
                transferContent,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(checkout.AccountNumber) ||
                string.IsNullOrWhiteSpace(checkout.BankShortName))
            {
                return TournamentPaymentServiceResult.Fail(
                    "Chưa cấu hình tài khoản nhận tiền Sepay.",
                    StatusCodes.Status500InternalServerError);
            }

            var currency = NormalizeCurrency(registration.Tournament.RegistrationFeeCurrency);
            var expiresAt = _options.PaymentExpireMinutes > 0
                ? now.AddMinutes(_options.PaymentExpireMinutes)
                : (DateTime?)null;

            var payment = new TournamentRegistrationPayment
            {
                RegistrationId = registration.RegistrationId,
                TournamentId = registration.TournamentId,
                UserId = userId,
                Provider = "sepay",
                PaymentMethod = "qr_transfer",
                Status = "pending",
                TransactionCode = transactionCode,
                BankCode = checkout.BankShortName,
                BankAccountNo = checkout.AccountNumber,
                BankAccountName = checkout.AccountName,
                QrImageUrl = checkout.QrImageUrl,
                TransferContent = transferContent,
                Amount = feeAmount,
                Currency = currency,
                RawResponse = JsonSerializer.Serialize(new
                {
                    transactionCode,
                    registration.RegistrationId,
                    registration.TournamentId,
                    registration.Tournament.Title,
                    amount = feeAmount,
                    currency,
                    checkout.BankShortName,
                    checkout.AccountNumber,
                    checkout.AccountName,
                    checkout.ResolvedByApi,
                    checkout.ProviderRawResponse
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                ExpiredAt = expiresAt,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.TournamentRegistrationPayments.Add(payment);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return TournamentPaymentServiceResult.Ok(
                MapCheckoutResponse(payment, registration, reusedExistingPayment: false),
                "Đã tạo mã thanh toán.");
        });
    }

    public async Task<TournamentPaymentCheckoutResponse?> GetCheckoutByTransactionCodeAsync(
        string transactionCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeToken(transactionCode);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return null;
        }

        var payment = await _db.TournamentRegistrationPayments
            .AsNoTracking()
            .Include(item => item.Registration)
                .ThenInclude(item => item.Tournament)
            .FirstOrDefaultAsync(item => item.TransactionCode == normalizedCode, cancellationToken);

        return payment is null
            ? null
            : MapCheckoutResponse(payment, payment.Registration, reusedExistingPayment: true);
    }

    public async Task<TournamentPaymentStatusResponse?> GetStatusAsync(
        string transactionCode,
        CancellationToken cancellationToken = default)
    {
        var checkout = await GetCheckoutByTransactionCodeAsync(transactionCode, cancellationToken);
        return checkout is null ? null : MapStatusResponse(checkout);
    }

    public async Task<TournamentPaymentWebhookResult> HandleSepayWebhookAsync(
        SepayWebhookPayload payload,
        string rawPayload,
        string? authorizationHeader,
        string? apiKeyHeader,
        CancellationToken cancellationToken = default)
    {
        if (!IsWebhookAuthorized(authorizationHeader, apiKeyHeader))
        {
            return TournamentPaymentWebhookResult.Unauthorized();
        }

        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var now = DateTime.UtcNow;
            var webhook = new TournamentSepayWebhook
            {
                Gateway = string.IsNullOrWhiteSpace(payload.Gateway) ? "sepay" : payload.Gateway.Trim(),
                EventType = payload.TransferType,
                ReferenceCode = payload.ReferenceCode,
                AccountNumber = payload.AccountNumber,
                Code = payload.Code,
                ContentTransfer = payload.Content,
                Description = payload.Description,
                TransferType = payload.TransferType,
                Amount = payload.TransferAmount,
                RawPayload = rawPayload,
                IsProcessed = false,
                CreatedAt = now
            };

            _db.TournamentSepayWebhooks.Add(webhook);

            if (!string.Equals(payload.TransferType, "in", StringComparison.OrdinalIgnoreCase) ||
                payload.TransferAmount <= 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return TournamentPaymentWebhookResult.Ignored("Webhook đã ghi nhận, nhưng không phải giao dịch tiền vào.");
            }

            if (!IsReceiverAccountMatch(payload.AccountNumber))
            {
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return TournamentPaymentWebhookResult.Ignored("Webhook không thuộc tài khoản nhận tiền đã cấu hình.");
            }

            var payment = await FindPaymentForWebhookAsync(payload, cancellationToken);
            if (payment is null)
            {
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return TournamentPaymentWebhookResult.Ignored("Không tìm thấy giao dịch thanh toán phù hợp.");
            }

            webhook.PaymentId = payment.PaymentId;

            if (string.Equals(payment.Status, "paid", StringComparison.OrdinalIgnoreCase) && payment.Registration.Paid)
            {
                webhook.IsProcessed = true;
                webhook.ProcessedAt = now;
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                await PublishRealtimeStatusAsync(payment.TransactionCode, cancellationToken);
                return TournamentPaymentWebhookResult.ProcessedResult(payment.TransactionCode, payment.RegistrationId);
            }

            payment.Status = "paid";
            payment.ProviderTransactionId = string.IsNullOrWhiteSpace(payload.ReferenceCode)
                ? payload.Id?.ToString(CultureInfo.InvariantCulture)
                : payload.ReferenceCode.Trim();
            payment.PaidAmount = NormalizeAmount(payload.TransferAmount);
            payment.PaidAt = now;
            payment.RawResponse = rawPayload;
            payment.UpdatedAt = now;

            payment.Registration.Paid = true;
            payment.Registration.PaidAt = now;
            payment.Registration.PaymentAmount = payment.Amount;

            webhook.IsProcessed = true;
            webhook.ProcessedAt = now;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await PublishRealtimeStatusAsync(payment.TransactionCode, cancellationToken);

            return TournamentPaymentWebhookResult.ProcessedResult(payment.TransactionCode, payment.RegistrationId);
        });
    }

    private async Task<TournamentRegistrationPayment?> FindReusablePaymentAsync(
        long registrationId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        return await _db.TournamentRegistrationPayments
            .Where(item => item.RegistrationId == registrationId &&
                           item.Provider == "sepay" &&
                           item.Amount == amount &&
                           ReusableStatuses.Contains(item.Status))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<TournamentRegistrationPayment?> FindPaymentForWebhookAsync(
        SepayWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        var normalizedAmount = NormalizeAmount(payload.TransferAmount);
        var candidateTokens = ExtractCandidateTokens(
            payload.Code,
            payload.Content,
            payload.Description,
            payload.ReferenceCode);

        if (candidateTokens.Length > 0)
        {
            var exactMatch = await _db.TournamentRegistrationPayments
                .Include(item => item.Registration)
                    .ThenInclude(item => item.Tournament)
                .Where(item => item.Provider == "sepay" &&
                               item.Amount == normalizedAmount &&
                               ReusableStatuses.Contains(item.Status) &&
                               (candidateTokens.Contains(item.TransactionCode) ||
                                (item.TransferContent != null && candidateTokens.Contains(item.TransferContent))))
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (exactMatch is not null)
            {
                return exactMatch;
            }
        }

        var normalizedContent = NormalizeCombinedContent(
            payload.Code,
            payload.Content,
            payload.Description,
            payload.ReferenceCode);
        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            return null;
        }

        var candidates = await _db.TournamentRegistrationPayments
            .Include(item => item.Registration)
                .ThenInclude(item => item.Tournament)
            .Where(item => item.Provider == "sepay" &&
                           item.Amount == normalizedAmount &&
                           ReusableStatuses.Contains(item.Status))
            .OrderByDescending(item => item.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var transactionCode = NormalizeToken(candidate.TransactionCode);
            var transferContent = NormalizeToken(candidate.TransferContent);

            if (!string.IsNullOrWhiteSpace(transactionCode) &&
                normalizedContent.Contains(transactionCode, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            if (!string.IsNullOrWhiteSpace(transferContent) &&
                normalizedContent.Contains(transferContent, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task PublishRealtimeStatusAsync(
        string transactionCode,
        CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var status = await GetStatusAsync(transactionCode, cancellationToken);
            if (status is null)
            {
                return;
            }

            await _publicRealtimeHub.BroadcastTournamentPaymentStatusUpdatedAsync(transactionCode, status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to publish tournament payment update for {TransactionCode}.", transactionCode);
        }
    }

    private async Task<string> GenerateUniqueTransactionCodeAsync(
        long tournamentId,
        long registrationId,
        CancellationToken cancellationToken)
    {
        var prefix = string.IsNullOrWhiteSpace(_options.TransferCodePrefix)
            ? "HNK"
            : NormalizeToken(_options.TransferCodePrefix) ?? "HNK";

        while (true)
        {
            var token = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var candidate = $"{prefix}T{tournamentId}R{registrationId}{token}";
            var exists = await _db.TournamentRegistrationPayments
                .AsNoTracking()
                .AnyAsync(item => item.TransactionCode == candidate, cancellationToken);

            if (!exists)
            {
                return candidate;
            }
        }
    }

    private TournamentPaymentCheckoutResponse MapCheckoutResponse(
        TournamentRegistrationPayment payment,
        TournamentRegistration registration,
        bool reusedExistingPayment)
    {
        var now = DateTime.UtcNow;
        var isPaid = string.Equals(payment.Status, "paid", StringComparison.OrdinalIgnoreCase) || registration.Paid;
        var isExpired = !isPaid && IsExpired(payment, now);
        var status = isPaid ? "paid" : isExpired ? "expired" : payment.Status;
        var tournament = registration.Tournament;
        var currency = NormalizeCurrency(payment.Currency);
        var teamName = BuildTeamName(registration);

        return new TournamentPaymentCheckoutResponse
        {
            Success = true,
            Message = reusedExistingPayment ? "Đã tải lại mã thanh toán." : "Đã tạo mã thanh toán.",
            ReusedExistingPayment = reusedExistingPayment,
            PaymentId = payment.PaymentId,
            RegistrationId = registration.RegistrationId,
            TournamentId = registration.TournamentId,
            TournamentTitle = tournament.Title,
            TeamName = teamName,
            Player1Name = registration.Player1Name,
            Player2Name = string.IsNullOrWhiteSpace(registration.Player2Name) ? null : registration.Player2Name,
            TransactionCode = payment.TransactionCode,
            PaymentStatus = status,
            IsPaid = isPaid,
            IsExpired = isExpired,
            StatusTitle = ResolveStatusTitle(isPaid, isExpired),
            StatusDescription = ResolveStatusDescription(isPaid, isExpired),
            Amount = payment.Amount,
            AmountText = FormatAmount(payment.Amount, currency),
            Currency = currency,
            ReceiverBankName = payment.BankCode ?? string.Empty,
            ReceiverBankShortName = payment.BankCode ?? string.Empty,
            ReceiverAccountNumber = payment.BankAccountNo ?? string.Empty,
            ReceiverAccountName = payment.BankAccountName ?? string.Empty,
            TransferContent = string.IsNullOrWhiteSpace(payment.TransferContent)
                ? payment.TransactionCode
                : payment.TransferContent,
            QrImageUrl = payment.QrImageUrl ?? string.Empty,
            CreatedAt = payment.CreatedAt,
            ExpiresAt = payment.ExpiredAt,
            PaidAt = payment.PaidAt ?? registration.PaidAt,
            PaidAtText = FormatDateTime(payment.PaidAt ?? registration.PaidAt),
            PollStatusUrl = $"/api/tournament-registration-payments/{Uri.EscapeDataString(payment.TransactionCode)}/status",
            RegistrationListUrl = $"/PickleballWeb/Tournament/{registration.TournamentId}/Registrations"
        };
    }

    private static TournamentPaymentStatusResponse MapStatusResponse(TournamentPaymentCheckoutResponse checkout)
    {
        return new TournamentPaymentStatusResponse(
            checkout.TransactionCode,
            checkout.PaymentStatus,
            checkout.IsPaid,
            checkout.IsExpired,
            checkout.StatusTitle,
            checkout.StatusDescription,
            checkout.PaidAt,
            checkout.PaidAtText);
    }

    private bool IsWebhookAuthorized(string? authorizationHeader, string? apiKeyHeader)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookApiKey))
        {
            return true;
        }

        var expected = _options.WebhookApiKey.Trim();
        if (string.Equals(apiKeyHeader?.Trim(), expected, StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedAuthorization = authorizationHeader?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedAuthorization))
        {
            return false;
        }

        return string.Equals(normalizedAuthorization, $"Apikey {expected}", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedAuthorization, $"Bearer {expected}", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedAuthorization, expected, StringComparison.Ordinal);
    }

    private bool IsReceiverAccountMatch(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(_options.ReceiverAccountNumber) ||
            string.IsNullOrWhiteSpace(accountNumber))
        {
            return true;
        }

        return string.Equals(
            NormalizeAccountNumber(_options.ReceiverAccountNumber),
            NormalizeAccountNumber(accountNumber),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool BelongsToRegistration(TournamentRegistration registration, long userId)
    {
        return registration.Player1UserId == userId || registration.Player2UserId == userId;
    }

    private static bool IsExpired(TournamentRegistrationPayment payment, DateTime now)
    {
        return payment.ExpiredAt.HasValue && payment.ExpiredAt.Value <= now;
    }

    private static string BuildTeamName(TournamentRegistration registration)
    {
        return string.IsNullOrWhiteSpace(registration.Player2Name)
            ? registration.Player1Name
            : $"{registration.Player1Name} / {registration.Player2Name}";
    }

    private static decimal NormalizeAmount(decimal amount)
    {
        return decimal.Round(amount, 0, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant();
    }

    private static string FormatAmount(decimal amount, string currency)
    {
        return string.Equals(currency, "VND", StringComparison.OrdinalIgnoreCase)
            ? string.Format(ViCulture, "{0:N0} VND", amount)
            : string.Format(ViCulture, "{0:N0} {1}", amount, currency);
    }

    private static string? FormatDateTime(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("HH:mm dd/MM/yyyy", ViCulture)
            : null;
    }

    private static string ResolveStatusTitle(bool isPaid, bool isExpired)
    {
        if (isPaid)
        {
            return "Đã thanh toán";
        }

        return isExpired ? "Mã thanh toán đã hết hạn" : "Đang chờ thanh toán";
    }

    private static string ResolveStatusDescription(bool isPaid, bool isExpired)
    {
        if (isPaid)
        {
            return "Bạn đã thanh toán thành công.";
        }

        return isExpired
            ? "Vui lòng quay lại danh sách đăng ký để tạo mã thanh toán mới."
            : "Chuyển khoản đúng số tiền và nội dung để hệ thống tự xác nhận.";
    }

    private static string NormalizeAccountNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Concat(value.Trim().Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }

    private static string NormalizeCombinedContent(params string?[] values)
    {
        return string.Concat(values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeToken(value) ?? string.Empty));
    }

    private static string[] ExtractCandidateTokens(params string?[] values)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var builder = new StringBuilder();
            foreach (var character in value.Trim().ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                    continue;
                }

                FlushCandidateToken(builder, tokens);
            }

            FlushCandidateToken(builder, tokens);
        }

        return tokens.ToArray();
    }

    private static void FlushCandidateToken(StringBuilder builder, ISet<string> tokens)
    {
        if (builder.Length >= 6)
        {
            tokens.Add(builder.ToString());
        }

        builder.Clear();
    }

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Concat(value
            .Trim()
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit));

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
