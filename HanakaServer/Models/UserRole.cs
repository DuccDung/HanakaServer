using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class UserRole
{
    public long UserId { get; set; }

    public int RoleId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Role Role { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
