using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class VideoPlayer
{
    public long VideoId { get; set; }

    public byte Slot { get; set; }

    public long? UserId { get; set; }

    public string? DisplayName { get; set; }

    public string? AvatarUrl { get; set; }

    public virtual User? User { get; set; }

    public virtual Video Video { get; set; } = null!;
}
