namespace HanakaServer.Options;

public sealed class RelayOptions
{
    // Staged preparation: admin lineups/library, public roster projection and legacy write guards.
    // Does not enable relay competition/scoring APIs. Set via Relay__AdminPreviewEnabled
    // only after applying both additive relay SQL scripts locally.
    public bool AdminPreviewEnabled { get; set; }
}
