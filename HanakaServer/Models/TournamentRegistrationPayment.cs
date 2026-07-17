using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class TournamentRegistrationPayment
{
    public long PaymentId { get; set; }

    public long RegistrationId { get; set; }

    public long TournamentId { get; set; }

    public long? UserId { get; set; }

    public string Provider { get; set; } = "sepay";

    public string PaymentMethod { get; set; } = "qr_transfer";

    public string Status { get; set; } = "pending";

    public string TransactionCode { get; set; } = null!;

    public string? ProviderTransactionId { get; set; }

    public string? BankCode { get; set; }

    public string? BankAccountNo { get; set; }

    public string? BankAccountName { get; set; }

    public string? QrImageUrl { get; set; }

    public string? TransferContent { get; set; }

    public decimal Amount { get; set; }

    public decimal? PaidAmount { get; set; }

    public string Currency { get; set; } = "VND";

    public string? RawResponse { get; set; }

    public DateTime? ExpiredAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual TournamentRegistration Registration { get; set; } = null!;

    public virtual Tournament Tournament { get; set; } = null!;

    public virtual User? User { get; set; }

    public virtual ICollection<TournamentSepayWebhook> TournamentSepayWebhooks { get; set; } = new List<TournamentSepayWebhook>();
}
