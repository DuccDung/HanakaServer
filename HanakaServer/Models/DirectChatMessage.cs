using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class DirectChatMessage
{
    public long DirectChatMessageId { get; set; }

    public long DirectChatRoomId { get; set; }

    public long SenderUserId { get; set; }

    public string MessageType { get; set; } = null!;

    public string? Content { get; set; }

    public string? MediaUrl { get; set; }

    public long? ReplyToMessageId { get; set; }

    public Guid? ClientMessageId { get; set; }

    public DateTime SentAt { get; set; }

    public bool IsRecalled { get; set; }

    public DateTime? RecalledAt { get; set; }

    public long? RecalledByUserId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual DirectChatRoom DirectChatRoom { get; set; } = null!;

    public virtual User SenderUser { get; set; } = null!;

    public virtual DirectChatMessage? ReplyToMessage { get; set; }

    public virtual ICollection<DirectChatMessage> InverseReplyToMessage { get; set; } = new List<DirectChatMessage>();

    public virtual User? RecalledByUser { get; set; }

    public virtual ICollection<DirectChatRoomParticipant> ReadByParticipants { get; set; } = new List<DirectChatRoomParticipant>();

    public virtual ICollection<UserBlock> UserBlocks { get; set; } = new List<UserBlock>();
}
