using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class ClubMessage
{
    public long MessageId { get; set; }

    public long ClubId { get; set; }

    public long SenderUserId { get; set; }

    public string MessageType { get; set; } = null!;

    public string? Content { get; set; }

    public string? MediaUrl { get; set; }

    public long? ReplyToId { get; set; }

    public DateTime SentAt { get; set; }

    public bool IsDeleted { get; set; }

    public virtual Club Club { get; set; } = null!;

    public virtual ICollection<ClubMessage> InverseReplyTo { get; set; } = new List<ClubMessage>();

    public virtual ICollection<ModerationReport> ModerationReports { get; set; } = new List<ModerationReport>();

    public virtual ClubMessage? ReplyTo { get; set; }

    public virtual User SenderUser { get; set; } = null!;

    public virtual ICollection<UserBlock> UserBlocks { get; set; } = new List<UserBlock>();
}
