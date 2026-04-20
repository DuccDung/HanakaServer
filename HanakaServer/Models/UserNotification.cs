using System;

namespace HanakaServer.Models;

public partial class UserNotification
{
    public long NotificationId { get; set; }

    public long UserId { get; set; }

    public string NotificationType { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Body { get; set; } = null!;

    public string? RefType { get; set; }

    public long? RefId { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? PayloadJson { get; set; }

    public virtual User User { get; set; } = null!;
}
