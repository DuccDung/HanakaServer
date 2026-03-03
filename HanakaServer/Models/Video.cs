using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class Video
{
    public long VideoId { get; set; }

    public string? ExternalId { get; set; }

    public string VideoType { get; set; } = null!;

    public string? BannerUrl { get; set; }

    public string? DateTimeRaw { get; set; }

    public DateTime? VideoTime { get; set; }

    public string? Code { get; set; }

    public string Title { get; set; } = null!;

    public int Score1 { get; set; }

    public int Score2 { get; set; }

    public int Score3 { get; set; }

    public int Score4 { get; set; }

    public virtual ICollection<VideoPlayer> VideoPlayers { get; set; } = new List<VideoPlayer>();
}
