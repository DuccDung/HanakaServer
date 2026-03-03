using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class CourtImage
{
    public long CourtImageId { get; set; }

    public long CourtId { get; set; }

    public string ImageUrl { get; set; } = null!;

    public int SortOrder { get; set; }

    public virtual Court Court { get; set; } = null!;
}
