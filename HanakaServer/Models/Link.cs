using System;

namespace HanakaServer.Models;

public partial class Link
{
    public long LinkId { get; set; }

    public string Url { get; set; } = null!;

    public string Type { get; set; } = null!;
}