using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class Court
{
    public long CourtId { get; set; }

    public string? ExternalId { get; set; }

    public string CourtName { get; set; } = null!;

    public string? AreaText { get; set; }

    public string? ManagerName { get; set; }

    public string? Phone { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<CourtImage> CourtImages { get; set; } = new List<CourtImage>();
}
