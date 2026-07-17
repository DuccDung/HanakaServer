using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace HanakaServer.Dtos.Payments;

public sealed class TournamentPaymentCheckoutResponse
{
    public bool Success { get; init; } = true;

    public string Message { get; init; } = string.Empty;

    public bool ReusedExistingPayment { get; init; }

    public long PaymentId { get; init; }

    public long RegistrationId { get; init; }

    public long TournamentId { get; init; }

    public string TournamentTitle { get; init; } = string.Empty;

    public string TeamName { get; init; } = string.Empty;

    public string Player1Name { get; init; } = string.Empty;

    public string? Player2Name { get; init; }

    public string TransactionCode { get; init; } = string.Empty;

    public string PaymentStatus { get; init; } = string.Empty;

    public bool IsPaid { get; init; }

    public bool IsExpired { get; init; }

    public string StatusTitle { get; init; } = string.Empty;

    public string StatusDescription { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string AmountText { get; init; } = string.Empty;

    public string Currency { get; init; } = "VND";

    public string ReceiverBankName { get; init; } = string.Empty;

    public string ReceiverBankShortName { get; init; } = string.Empty;

    public string ReceiverAccountNumber { get; init; } = string.Empty;

    public string ReceiverAccountName { get; init; } = string.Empty;

    public string TransferContent { get; init; } = string.Empty;

    public string QrImageUrl { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }

    public DateTime? ExpiresAt { get; init; }

    public DateTime? PaidAt { get; init; }

    public string? PaidAtText { get; init; }

    public string PollStatusUrl { get; init; } = string.Empty;

    public string RegistrationListUrl { get; init; } = string.Empty;
}

public sealed record TournamentPaymentStatusResponse(
    string TransactionCode,
    string PaymentStatus,
    bool IsPaid,
    bool IsExpired,
    string StatusTitle,
    string StatusDescription,
    DateTime? PaidAt,
    string? PaidAtText);

public sealed class SepayWebhookPayload
{
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("gateway")]
    public string? Gateway { get; set; }

    [JsonPropertyName("transactionDate")]
    public string? TransactionDate { get; set; }

    [JsonPropertyName("accountNumber")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("transferType")]
    public string? TransferType { get; set; }

    [JsonPropertyName("transferAmount")]
    public decimal TransferAmount { get; set; }

    [JsonPropertyName("accumulated")]
    public decimal? Accumulated { get; set; }

    [JsonPropertyName("subAccount")]
    public string? SubAccount { get; set; }

    [JsonPropertyName("referenceCode")]
    public string? ReferenceCode { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed record TournamentPaymentServiceResult(
    bool Success,
    string Message,
    int StatusCode,
    TournamentPaymentCheckoutResponse? Payment)
{
    public static TournamentPaymentServiceResult Ok(TournamentPaymentCheckoutResponse payment, string message = "")
    {
        return new TournamentPaymentServiceResult(true, message, StatusCodes.Status200OK, payment);
    }

    public static TournamentPaymentServiceResult Fail(string message, int statusCode = StatusCodes.Status400BadRequest)
    {
        return new TournamentPaymentServiceResult(false, message, statusCode, null);
    }
}

public sealed record TournamentPaymentWebhookResult(
    bool Authorized,
    bool Success,
    bool Processed,
    string Message,
    string? TransactionCode,
    long? RegistrationId)
{
    public static TournamentPaymentWebhookResult Unauthorized()
    {
        return new TournamentPaymentWebhookResult(false, false, false, "Webhook không hợp lệ.", null, null);
    }

    public static TournamentPaymentWebhookResult Ignored(string message)
    {
        return new TournamentPaymentWebhookResult(true, true, false, message, null, null);
    }

    public static TournamentPaymentWebhookResult ProcessedResult(string transactionCode, long registrationId)
    {
        return new TournamentPaymentWebhookResult(true, true, true, "Đã ghi nhận thanh toán.", transactionCode, registrationId);
    }
}
