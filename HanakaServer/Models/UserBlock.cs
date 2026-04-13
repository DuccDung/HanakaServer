using System;

namespace HanakaServer.Models;

public partial class UserBlock
{
    public long BlockId { get; set; }

    public long BlockerUserId { get; set; }

    public long BlockedUserId { get; set; }

    public long? SourceClubId { get; set; }

    public long? SourceMessageId { get; set; }

    public long? ReportId { get; set; }

    public string ReasonCode { get; set; } = null!;

    public string? Notes { get; set; }

    public string Source { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime BlockedAt { get; set; }

    public DateTime? UnblockedAt { get; set; }

    public virtual User BlockedUser { get; set; } = null!;

    public virtual User BlockerUser { get; set; } = null!;

    public virtual ModerationReport? Report { get; set; }

    public virtual Club? SourceClub { get; set; }

    public virtual ClubMessage? SourceMessage { get; set; }
}
