using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class MatchPlayer
{
    public long MatchId { get; set; }

    public string Side { get; set; } = null!;

    public byte Slot { get; set; }

    public long? UserId { get; set; }

    public string? DisplayName { get; set; }

    public string? AvatarUrl { get; set; }

    public virtual Match Match { get; set; } = null!;

    public virtual User? User { get; set; }
}
