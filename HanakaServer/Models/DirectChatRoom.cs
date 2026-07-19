using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class DirectChatRoom
{
    public long DirectChatRoomId { get; set; }

    public long User1Id { get; set; }

    public long User2Id { get; set; }

    public long? LastMessageId { get; set; }

    public DateTime? LastMessageAt { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User User1 { get; set; } = null!;

    public virtual User User2 { get; set; } = null!;

    public virtual DirectChatMessage? LastMessage { get; set; }

    public virtual ICollection<DirectChatMessage> DirectChatMessages { get; set; } = new List<DirectChatMessage>();

    public virtual ICollection<DirectChatRoomParticipant> DirectChatRoomParticipants { get; set; } = new List<DirectChatRoomParticipant>();

    public virtual ICollection<UserBlock> UserBlocks { get; set; } = new List<UserBlock>();
}
