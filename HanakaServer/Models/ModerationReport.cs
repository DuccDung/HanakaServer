using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class ModerationReport
{
    public long ReportId { get; set; }

    public long ReporterUserId { get; set; }

    public long? TargetUserId { get; set; }

    public long? ClubId { get; set; }

    public long? MessageId { get; set; }

    public string ReportType { get; set; } = null!;

    public string ReasonCode { get; set; } = null!;

    public string? ReasonLabel { get; set; }

    public string? Notes { get; set; }

    public string? MessageContentSnapshot { get; set; }

    public string? ReporterNameSnapshot { get; set; }

    public string? TargetNameSnapshot { get; set; }

    public string Source { get; set; } = null!;

    public string Status { get; set; } = null!;

    public bool DeveloperNotified { get; set; }

    public DateTime? DeveloperNotifiedAt { get; set; }

    public long? ReviewedByUserId { get; set; }

    public string? ReviewedByName { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string ResolutionAction { get; set; } = null!;

    public string? ResolutionNote { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime SlaDueAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Club? Club { get; set; }

    public virtual ClubMessage? Message { get; set; }

    public virtual User ReporterUser { get; set; } = null!;

    public virtual User? ReviewedByUser { get; set; }

    public virtual User? TargetUser { get; set; }

    public virtual ICollection<UserBlock> UserBlocks { get; set; } = new List<UserBlock>();
}
