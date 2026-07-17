using System;

namespace HanakaServer.Models;

public partial class TournamentSepayWebhook
{
    public long SepayWebhookId { get; set; }

    public long? PaymentId { get; set; }

    public string Gateway { get; set; } = "sepay";

    public string? EventType { get; set; }

    public string? ReferenceCode { get; set; }

    public string? AccountNumber { get; set; }

    public string? Code { get; set; }

    public string? ContentTransfer { get; set; }

    public string? Description { get; set; }

    public string? TransferType { get; set; }

    public decimal? Amount { get; set; }

    public string RawPayload { get; set; } = null!;

    public bool IsProcessed { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual TournamentRegistrationPayment? Payment { get; set; }
}
