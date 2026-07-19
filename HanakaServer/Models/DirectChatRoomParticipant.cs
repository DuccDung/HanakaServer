using System;

namespace HanakaServer.Models;

public partial class DirectChatRoomParticipant
{
    public long DirectChatRoomId { get; set; }

    public long UserId { get; set; }

    public long? LastReadMessageId { get; set; }

    public DateTime? LastReadAt { get; set; }

    public bool IsArchived { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public bool IsMuted { get; set; }

    public DateTime? MutedUntil { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual DirectChatRoom DirectChatRoom { get; set; } = null!;

    public virtual User User { get; set; } = null!;

    public virtual DirectChatMessage? LastReadMessage { get; set; }
}
