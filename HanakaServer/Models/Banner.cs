using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class Banner
{
    public long BannerId { get; set; }

    public string BannerKey { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string ImageUrl { get; set; } = null!;

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }
}
