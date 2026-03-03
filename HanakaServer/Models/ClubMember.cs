using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class ClubMember
{
    public long ClubId { get; set; }

    public long UserId { get; set; }

    public string MemberRole { get; set; } = null!;

    public DateTime JoinedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual Club Club { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
